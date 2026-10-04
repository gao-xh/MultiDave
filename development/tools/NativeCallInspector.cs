using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.Loader;
using System.Security.Cryptography;
using System.Text.Json;
using AssetRipper.Primitives;
using Iced.Intel;
using LibCpp2IL;
using LibCpp2IL.Metadata;

// Runs as a separate CLR process. Reads the original files as data; never loads game code.
internal static class NativeCallInspector
{
    private static int Main(string[] args)
    {
        try
        {
            if (args.Length != 1) throw new ArgumentException("Expected a local request JSON path.");
            var request = JsonSerializer.Deserialize<InspectionRequest>(File.ReadAllText(args[0]));
            if (request == null) throw new ArgumentException("Empty request.");
            AssemblyLoadContext.Default.Resolving += (_, name) =>
            {
                // Only dependencies of the installed offline parser. No game or interop assembly.
                if (name.Name != "AssetRipper.Primitives" && name.Name != "WasmDisassembler" &&
                    name.Name != "Iced" && name.Name != "LibCpp2IL") return null;
                return AssemblyLoadContext.Default.LoadFromAssemblyPath(Path.Combine(request.CorePath, name.Name + ".dll"));
            };
            return Inspect(request);
        }
        catch (Exception error)
        {
            Console.Error.WriteLine("Native inspection failed: " + error.GetType().Name + ": " + error.Message);
            return 1;
        }
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static int Inspect(InspectionRequest request)
    {
        if (request.Depth < 0 || request.Depth > 3 || request.MaxMethods < 1 || request.MaxMethods > 256 ||
            request.MaxInstructions < 64 || request.MaxInstructions > 32768 ||
            request.Selectors == null || request.Selectors.Length < 1 || request.Selectors.Length > 16)
            throw new ArgumentException("Invalid analysis bounds.");
        var binaryHash = FileHash(request.BinaryPath);
        var metadataHash = FileHash(request.MetadataPath);
        var report = new InspectionReport
        {
            StartedUtc = DateTimeOffset.UtcNow.ToString("o"), UnityVersion = request.UnityVersion,
            BinarySha256 = binaryHash, MetadataSha256 = metadataHash, RequestedDepth = request.Depth,
            MaxMethods = request.MaxMethods, MaxInstructions = request.MaxInstructions,
            ParserAssemblyVersion = typeof(LibCpp2IlMain).Assembly.GetName().Version.ToString(),
            DisassemblerAssemblyVersion = typeof(Decoder).Assembly.GetName().Version.ToString()
        };
        LibCpp2IlMain.Settings.AllowManualMetadataAndCodeRegInput = false;
        LibCpp2IlMain.Settings.DisableGlobalResolving = true;
        var originalOut = Console.Out;
        var originalError = Console.Error;
        bool loaded;
        try
        {
            Console.SetOut(TextWriter.Null);
            Console.SetError(TextWriter.Null);
            loaded = LibCpp2IlMain.LoadFromFile(request.BinaryPath, request.MetadataPath, UnityVersion.Parse(request.UnityVersion));
        }
        finally
        {
            Console.SetOut(originalOut);
            Console.SetError(originalError);
        }
        if (!loaded) throw new InvalidDataException("IL2CPP parser could not initialize.");
        var binaryBytes = LibCpp2IlMain.Binary.GetRawBinaryContent();
        // Parse PE exception-directory ranges independently rather than guess a body from the next managed address.
        var pe = new PeRanges(binaryBytes);
        report.MetadataVersion = LibCpp2IlMain.TheMetadata.MetadataVersion;
        report.MetadataMethodCount = LibCpp2IlMain.TheMetadata.MethodDefinitionCount;
        report.RuntimeFunctionCount = pe.Functions.Length;
        report.UnsupportedUnwindRanges = pe.UnsupportedUnwindRanges;
        var pending = new Queue<PendingMethod>();
        var enqueued = new HashSet<string>(StringComparer.Ordinal);
        Action<Il2CppMethodDefinition, int, string> enqueue = (method, depth, reason) =>
        {
            string key = MethodKey(method);
            if (enqueued.Contains(key)) return;
            if (enqueued.Count >= request.MaxMethods)
            {
                report.MethodLimitReached = true;
                if (depth == 0) report.OmittedRootMethods++;
                return;
            }
            enqueued.Add(key);
            pending.Enqueue(new PendingMethod { Method = method, Depth = depth, Reason = reason });
        };
        foreach (var selector in request.Selectors)
        {
            var separator = selector.IndexOf("::", StringComparison.Ordinal);
            if (separator < 1 || separator + 2 == selector.Length || selector.IndexOf("::", separator + 2, StringComparison.Ordinal) >= 0)
                throw new ArgumentException("Invalid method selector: " + selector);
            var typeName = selector.Substring(0, separator);
            var methodName = selector.Substring(separator + 2);
            var types = LibCpp2IlMain.TheMetadata.typeDefs.Where(type => type.FullName == typeName).ToArray();
            if (types.Length != 1) throw new ArgumentException("Selector type must resolve exactly once: " + typeName);
            var methods = types[0].Methods.Where(method => method.Name == methodName).ToArray();
            if (methods.Length == 0) throw new ArgumentException("Selector method does not exist: " + selector);
            report.Roots.Add(new RootSelection { Selector = selector, MatchedOverloads = methods.Length });
            foreach (var method in methods) enqueue(method, 0, "Exact selector");
            // Name association is metadata evidence, not proof of a runtime factory/MoveNext execution edge.
            foreach (var nested in LibCpp2IlMain.TheMetadata.typeDefs.Where(type =>
                type.DeclaringType != null && type.DeclaringType.FullName == typeName &&
                type.Name.StartsWith("<" + methodName + ">d__", StringComparison.Ordinal)))
            {
                foreach (var moveNext in nested.Methods.Where(method => method.Name == "MoveNext"))
                    enqueue(moveNext, 0, "Compiler state-machine name association with " + selector);
            }
        }
        while (pending.Count != 0)
        {
            var next = pending.Dequeue();
            var method = next.Method;
            var row = new MethodInspection
            {
                Type = method.DeclaringType.FullName, Signature = method.HumanReadableSignature,
                EntryRva = Hex(method.Rva), Depth = next.Depth, IncludedBecause = next.Reason
            };
            report.Methods.Add(row);
            if (method.MethodPointer == 0) { row.BodyStatus = "No native pointer"; continue; }
            ulong entry = method.MethodPointer;
            if (entry < pe.ImageBase || entry - pe.ImageBase > uint.MaxValue)
            { row.BodyStatus = "Entry outside PE image address space"; continue; }
            uint entryRva = (uint)(entry - pe.ImageBase);
            var function = pe.Find(entryRva);
            if (function == null) { row.BodyStatus = "No containing runtime-function range; leaf body not guessed"; continue; }
            row.RangeStartRva = Hex(function.Start);
            row.RangeEndRva = Hex(function.End);
            row.EntryMatchesRangeStart = entryRva == function.Start;
            var family = pe.Family(function);
            row.KnownUnwindFamilyRanges = family.Length;
            row.UnwindInfoSupported = function.ChainKnown;
            row.EntryMatchesUnwindFamilyRoot = entryRva == function.Root.Start;
            if (!row.EntryMatchesRangeStart || !row.EntryMatchesUnwindFamilyRoot)
            { row.BodyStatus = "Interior or secondary-family entry; ownership not guessed"; continue; }
            if (family.Length > 32 || family.Sum(range => (long)range.End - range.Start) > 262144)
            { row.BodyStatus = "Unwind-family ranges exceed fragment/byte bounds"; continue; }
            foreach (var fragment in family)
            {
                // Only fragments linked by exact UNW_FLAG_CHAININFO tuples; adjacency is never ownership evidence.
                uint start = fragment == function ? entryRva : fragment.Start;
                int length = checked((int)(fragment.End - start));
                int offset = pe.Map(start, length, true);
                var code = new byte[length];
                Buffer.BlockCopy(binaryBytes, offset, code, 0, length);
                var decoder = Decoder.Create(64, new ByteArrayCodeReader(code));
                decoder.IP = pe.ImageBase + start;
                var fragmentRow = new DecodedRange { StartRva = Hex(start), EndRva = Hex(fragment.End),
                    IncludedBecause = fragment == function ? "Containing entry range" : "Exact chained unwind family" };
                row.Ranges.Add(fragmentRow);
                FlowControl lastFlow = FlowControl.Next;
                while (decoder.IP < pe.ImageBase + fragment.End && row.InstructionCount < request.MaxInstructions)
                {
                    var instruction = decoder.Decode();
                    row.InstructionCount++;
                    fragmentRow.InstructionCount++;
                    if (instruction.Code == Code.INVALID) { row.InvalidInstruction = true; break; }
                    if (instruction.NextIP > pe.ImageBase + fragment.End) { row.RangeOverrun = true; break; }
                    lastFlow = instruction.FlowControl;
                    if (instruction.FlowControl == FlowControl.IndirectCall) { row.IndirectCalls++; continue; }
                    if (instruction.FlowControl == FlowControl.IndirectBranch) { row.IndirectBranches++; continue; }
                    bool call = instruction.FlowControl == FlowControl.Call;
                    bool branch = instruction.FlowControl == FlowControl.UnconditionalBranch || instruction.FlowControl == FlowControl.ConditionalBranch;
                    if (!call && !branch) continue;
                    if (instruction.Op0Kind != OpKind.NearBranch64 && instruction.Op0Kind != OpKind.NearBranch32 &&
                        instruction.Op0Kind != OpKind.NearBranch16) { row.UnresolvedFlowInstructions++; continue; }
                    ulong target = instruction.NearBranchTarget;
                    bool outsideFamily = target < pe.ImageBase || !family.Any(range =>
                        target - pe.ImageBase >= range.Start && target - pe.ImageBase < range.End);
                    if (!call && !outsideFamily) continue;
                    var edge = new CallEdge
                    {
                        InstructionRva = Hex(instruction.IP - pe.ImageBase),
                        Kind = call ? "Direct call" : instruction.FlowControl == FlowControl.ConditionalBranch ?
                            "External conditional branch (ownership unresolved)" : "External unconditional branch (tail-call candidate)",
                        TargetRva = target >= pe.ImageBase ? Hex(target - pe.ImageBase) : "Outside image"
                    };
                    row.Edges.Add(edge);
                    List<Il2CppMethodDefinition> aliases;
                    if (!LibCpp2IlMain.MethodsByPtr.TryGetValue(target, out aliases) || aliases == null || aliases.Count == 0)
                    { edge.Resolution = "No exact managed-method entry; native helper/thunk not resolved"; continue; }
                    edge.Resolution = "Exact method-pointer match; shared-address aliases preserved; generic instance unresolved";
                    edge.TotalAliases = aliases.Count;
                    edge.AliasesTruncated = aliases.Count > 16;
                    foreach (var alias in aliases.Take(16))
                    {
                        edge.Callees.Add(alias.DeclaringType.FullName + "::" + alias.HumanReadableSignature);
                        if (next.Depth < request.Depth && instruction.FlowControl != FlowControl.ConditionalBranch)
                            enqueue(alias, next.Depth + 1, "Exact direct target from " + MethodKey(method));
                    }
                }
                fragmentRow.RangeDecoded = decoder.IP == pe.ImageBase + fragment.End && !row.InvalidInstruction && !row.RangeOverrun;
                fragmentRow.FallthroughOutsideKnownFamily = fragmentRow.RangeDecoded &&
                    (lastFlow == FlowControl.Next || lastFlow == FlowControl.Call || lastFlow == FlowControl.IndirectCall || lastFlow == FlowControl.ConditionalBranch) &&
                    !family.Any(range => range.Start == fragment.End);
                if (row.InvalidInstruction || row.RangeOverrun) break;
                if (row.InstructionCount >= request.MaxInstructions && (decoder.IP < pe.ImageBase + fragment.End || fragment != family[family.Length - 1]))
                {
                    row.InstructionLimitReached = true;
                    break;
                }
            }
            row.BodyStatus = row.InvalidInstruction || row.RangeOverrun || row.InstructionLimitReached ?
                "Partial bounded decoding" : "Known unwind-family ranges decoded; method completeness unproven";
        }
        if (binaryHash != FileHash(request.BinaryPath) || metadataHash != FileHash(request.MetadataPath))
            throw new IOException("Game files changed during analysis; no fresh report written.");
        report.FinishedUtc = DateTimeOffset.UtcNow.ToString("o");
        string json = JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true });
        var temporaryPath = request.OutputPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllText(temporaryPath, json + Environment.NewLine);
            File.Move(temporaryPath, request.OutputPath, true);
        }
        finally { if (File.Exists(temporaryPath)) File.Delete(temporaryPath); }
        Console.WriteLine("Read-only analysis: " + report.Methods.Count + " methods; metadata " + report.MetadataVersion +
            "; method limit reached=" + report.MethodLimitReached + ". Static edges are not runtime execution evidence.");
        return 0;
    }

    private static string FileHash(string path)
    {
        using var input = File.OpenRead(path);
        using var sha = SHA256.Create();
        return Convert.ToHexString(sha.ComputeHash(input));
    }
    private static string MethodKey(Il2CppMethodDefinition method) => method.DeclaringType.FullName + "::" + method.HumanReadableSignature + "@" + Hex(method.Rva);
    private static string Hex(ulong value) => "0x" + value.ToString("X");
    private sealed class PendingMethod { public Il2CppMethodDefinition Method; public int Depth; public string Reason; }
}

internal sealed class InspectionRequest
{
    public string CorePath { get; set; }
    public string BinaryPath { get; set; }
    public string MetadataPath { get; set; }
    public string UnityVersion { get; set; }
    public string[] Selectors { get; set; }
    public string OutputPath { get; set; }
    public int Depth { get; set; }
    public int MaxMethods { get; set; }
    public int MaxInstructions { get; set; }
}
internal sealed class InspectionReport
{
    public string StartedUtc { get; set; }
    public string FinishedUtc { get; set; }
    public string UnityVersion { get; set; }
    public string BinarySha256 { get; set; }
    public string MetadataSha256 { get; set; }
    public string ParserAssemblyVersion { get; set; }
    public string DisassemblerAssemblyVersion { get; set; }
    public float MetadataVersion { get; set; }
    public int MetadataMethodCount { get; set; }
    public int RuntimeFunctionCount { get; set; }
    public int UnsupportedUnwindRanges { get; set; }
    public int RequestedDepth { get; set; }
    public int MaxMethods { get; set; }
    public int MaxInstructions { get; set; }
    public bool MethodLimitReached { get; set; }
    public int OmittedRootMethods { get; set; }
    public bool GameCodeExecuted { get; set; } = false;
    public string EvidenceScope { get; set; } = "Static x64 direct targets in PE runtime-function ranges and exactly chained version-1 unwind families. No full method-body coverage, reachable control-flow paths, data flow, generic instance or virtual/delegate resolution, runtime ordering, branch outcomes, capture success, storage delta, or save completion proven.";
    public List<RootSelection> Roots { get; set; } = new List<RootSelection>();
    public List<MethodInspection> Methods { get; set; } = new List<MethodInspection>();
}
internal sealed class RootSelection { public string Selector { get; set; } public int MatchedOverloads { get; set; } }
internal sealed class MethodInspection
{
    public string Type { get; set; }
    public string Signature { get; set; }
    public string EntryRva { get; set; }
    public int Depth { get; set; }
    public string IncludedBecause { get; set; }
    public string BodyStatus { get; set; }
    public string RangeStartRva { get; set; }
    public string RangeEndRva { get; set; }
    public bool EntryMatchesRangeStart { get; set; }
    public int KnownUnwindFamilyRanges { get; set; }
    public bool UnwindInfoSupported { get; set; }
    public bool EntryMatchesUnwindFamilyRoot { get; set; }
    public int InstructionCount { get; set; }
    public int IndirectCalls { get; set; }
    public int IndirectBranches { get; set; }
    public int UnresolvedFlowInstructions { get; set; }
    public bool InvalidInstruction { get; set; }
    public bool RangeOverrun { get; set; }
    public bool InstructionLimitReached { get; set; }
    public List<CallEdge> Edges { get; set; } = new List<CallEdge>();
    public List<DecodedRange> Ranges { get; set; } = new List<DecodedRange>();
}
internal sealed class DecodedRange
{
    public string StartRva { get; set; }
    public string EndRva { get; set; }
    public string IncludedBecause { get; set; }
    public int InstructionCount { get; set; }
    public bool RangeDecoded { get; set; }
    public bool FallthroughOutsideKnownFamily { get; set; }
}
internal sealed class CallEdge
{
    public string InstructionRva { get; set; }
    public string Kind { get; set; }
    public string TargetRva { get; set; }
    public string Resolution { get; set; }
    public int TotalAliases { get; set; }
    public bool AliasesTruncated { get; set; }
    public List<string> Callees { get; set; } = new List<string>();
}

internal sealed class PeRanges
{
    public ulong ImageBase { get; }
    public FunctionRange[] Functions { get; }
    public int UnsupportedUnwindRanges { get; private set; }
    private readonly byte[] bytes;
    private readonly List<Section> sections = new List<Section>();
    private readonly uint imageSize;
    private readonly Dictionary<uint, FunctionRange> byStart = new Dictionary<uint, FunctionRange>();
    private readonly Dictionary<uint, List<FunctionRange>> families = new Dictionary<uint, List<FunctionRange>>();
    public PeRanges(byte[] bytes)
    {
        this.bytes = bytes ?? throw new ArgumentNullException(nameof(bytes));
        if (U16(0) != 0x5A4D) throw new InvalidDataException("Not an MZ file.");
        int pe = checked((int)U32(0x3C));
        if (U32(pe) != 0x4550 || U16(pe + 4) != 0x8664) throw new InvalidDataException("Expected a Windows x64 PE image.");
        int sectionCount = U16(pe + 6);
        int optionalSize = U16(pe + 20);
        int optional = checked(pe + 24);
        if (optionalSize < 144 || U16(optional) != 0x20B || U32(optional + 108) < 4)
            throw new InvalidDataException("PE32+ exception directory is missing.");
        ImageBase = U64(optional + 24);
        imageSize = U32(optional + 56);
        uint tableRva = U32(optional + 112 + 3 * 8);
        uint tableSize = U32(optional + 112 + 3 * 8 + 4);
        if (sectionCount < 1 || sectionCount > 96 || tableSize == 0 || tableSize % 12 != 0 || tableSize / 12 > 2000000)
            throw new InvalidDataException("Invalid PE section or runtime-function table bounds.");
        for (int index = 0; index < sectionCount; index++)
        {
            int offset = checked(optional + optionalSize + index * 40);
            sections.Add(new Section { VirtualStart = U32(offset + 12), RawSize = U32(offset + 16), RawStart = U32(offset + 20),
                Executable = (U32(offset + 36) & 0x20000000) != 0 });
        }
        int table = Map(tableRva, checked((int)tableSize));
        Functions = new FunctionRange[tableSize / 12];
        for (int index = 0; index < Functions.Length; index++)
        {
            uint start = U32(table + index * 12), end = U32(table + index * 12 + 4);
            if (start >= end || end > imageSize || (index > 0 && start < Functions[index - 1].End))
                throw new InvalidDataException("Runtime-function ranges overlap, are unsorted, or exceed image bounds.");
            Functions[index] = new FunctionRange { Start = start, End = end, Unwind = U32(table + index * 12 + 8) };
            byStart.Add(start, Functions[index]);
        }
        foreach (var range in Functions) ResolveRoot(range, 0);
        foreach (var range in Functions.Where(range => range.ChainKnown))
        {
            List<FunctionRange> family;
            if (!families.TryGetValue(range.Root.Start, out family)) families.Add(range.Root.Start, family = new List<FunctionRange>());
            family.Add(range);
        }
    }
    private FunctionRange ResolveRoot(FunctionRange range, int depth)
    {
        if (range.Root != null) return range.Root;
        if (depth > 32 || range.Resolving) throw new InvalidDataException("Cyclic or excessive chained unwind depth.");
        range.Resolving = true;
        int offset = Map(range.Unwind, 4);
        int version = bytes[offset] & 7, flags = bytes[offset] >> 3;
        if (version != 1)
        {
            UnsupportedUnwindRanges++;
            range.Root = range;
            range.ChainKnown = false;
        }
        else if ((flags & 4) == 0)
        {
            range.Root = range;
            range.ChainKnown = true;
        }
        else
        {
            if ((flags & 3) != 0) throw new InvalidDataException("Invalid combined unwind-chain and handler flags.");
            int alignedCodes = (bytes[offset + 2] + 1) & ~1;
            int tail = checked(Map(range.Unwind, 4 + alignedCodes * 2 + 12) + 4 + alignedCodes * 2);
            uint parentStart = U32(tail), parentEnd = U32(tail + 4), parentUnwind = U32(tail + 8);
            FunctionRange parent;
            if (!byStart.TryGetValue(parentStart, out parent) || parent.End != parentEnd || parent.Unwind != parentUnwind)
                throw new InvalidDataException("Chained unwind tuple has no exact runtime-function match.");
            range.Root = ResolveRoot(parent, depth + 1);
            range.ChainKnown = parent.ChainKnown;
        }
        range.Resolving = false;
        return range.Root;
    }
    public FunctionRange[] Family(FunctionRange range) => range.ChainKnown ? families[range.Root.Start].ToArray() : new[] { range };
    public FunctionRange Find(uint rva)
    {
        int low = 0, high = Functions.Length - 1;
        while (low <= high)
        {
            int mid = low + (high - low) / 2;
            var range = Functions[mid];
            if (rva < range.Start) high = mid - 1;
            else if (rva >= range.End) low = mid + 1;
            else return range;
        }
        return null;
    }
    public int Map(uint rva, int length, bool executable = false)
    {
        if (length < 0 || (ulong)rva + (uint)length > imageSize) throw new InvalidDataException("RVA exceeds PE image bounds.");
        var matches = sections.Where(section => rva >= section.VirtualStart &&
            (ulong)rva + (uint)length <= (ulong)section.VirtualStart + section.RawSize).ToArray();
        if (matches.Length != 1) throw new InvalidDataException("RVA is not backed by exactly one file section.");
        if (executable && !matches[0].Executable) throw new InvalidDataException("Instruction range is outside executable sections.");
        ulong raw = (ulong)matches[0].RawStart + rva - matches[0].VirtualStart;
        if (raw + (uint)length > (ulong)bytes.Length || raw > int.MaxValue) throw new InvalidDataException("PE range exceeds file bounds.");
        return (int)raw;
    }
    private void Check(int offset, int length)
    {
        if (offset < 0 || offset > bytes.Length - length) throw new InvalidDataException("Truncated PE headers.");
    }
    private ushort U16(int offset) { Check(offset, 2); return BitConverter.ToUInt16(bytes, offset); }
    private uint U32(int offset) { Check(offset, 4); return BitConverter.ToUInt32(bytes, offset); }
    private ulong U64(int offset) { Check(offset, 8); return BitConverter.ToUInt64(bytes, offset); }
    private sealed class Section { public uint VirtualStart; public uint RawStart; public uint RawSize; public bool Executable; }
    internal sealed class FunctionRange
    {
        public uint Start; public uint End; public uint Unwind;
        public FunctionRange Root; public bool Resolving; public bool ChainKnown;
    }
}
