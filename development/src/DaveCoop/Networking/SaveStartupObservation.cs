using System;
using DaveCoop.Core.Guest;

namespace DaveCoop.Networking
{
    // Local logs only. No path text, account, exception message or wrapper.
    internal sealed class SaveStartupPathEvidence
    {
        public string Category { get; internal set; }
        public bool Present { get; internal set; }
        public int Utf16Length { get; internal set; }
        public string Sha256 { get; internal set; }
        public bool LimitExceeded { get; internal set; }
    }

    internal sealed class SaveStartupObservation
    {
        public SaveStartupCallObservation Trace { get; internal set; }
        public bool InstanceWrapperPresent { get; internal set; }
        public bool ReturnedIteratorWrapperPresent { get; internal set; }
        public bool DataArgumentWrapperPresent { get; internal set; }
        public bool CompletionDelegateWrapperPresent { get; internal set; }
        public bool? OriginalBoolReturn { get; internal set; }
        public int? SaveDataType { get; internal set; }
        public int? SlotIndex { get; internal set; }
        public int? SaveSlotType { get; internal set; }
        public bool NativeFieldSnapshotCopied { get; internal set; }
        public long? LocalInstanceOrdinal { get; internal set; }
        public long? CurrentIteratorOwnerOrdinal { get; internal set; }
        public int? IteratorState { get; internal set; }
        public bool? IsInitialized { get; internal set; }
        public bool? IsLoadFinished { get; internal set; }
        public bool? IsGameLoaded { get; internal set; }
        public bool? SkipCloudPullForPreset { get; internal set; }
        public bool? GameManagerPresent { get; internal set; }
        public bool? PlayerManagerPresent { get; internal set; }
        public bool? PhotoManagerPresent { get; internal set; }
        public bool? UserOptionManagerPresent { get; internal set; }
        public bool? ManagerDataPresent { get; internal set; }
        public bool? PlayerInteractionPresent { get; internal set; }
        public bool? PrefixIdentityMatched { get; internal set; }
        public SaveStartupPathEvidence[] Paths { get; internal set; } = Array.Empty<SaveStartupPathEvidence>();
        public string ReadError { get; internal set; }
        public bool ScalarEvidenceOnly => !NativeFieldSnapshotCopied;
        public bool ObservationOnly => true;
        public bool NativeIdentityLifetimeVerified => false;
        public bool NativeHookAbiVerified => false;
        public bool StartupCompleteness => false;
        public bool StartEarlyCoverageVerified => false;
        public bool FirstLoadOrderVerified => false;
        public bool PathIsolationVerified => false;
        public bool CloudIsolationVerified => false;
        public bool GuestStateIsolated => false;
        public bool NativePermission => false;
        public bool WorldAuthority => false;
        public bool CargoAuthority => false;
    }
}
