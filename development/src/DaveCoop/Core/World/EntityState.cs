using System;
using DaveCoop.Core.Protocol;

namespace DaveCoop.Core.World
{
    public enum EntityKind { Fish = 1, Item = 2 }

    public sealed class EntityState
    {
        public long Id { get; set; }
        public EntityKind Kind { get; set; }
        public int DataTid { get; set; }
        public Pose Root { get; set; }
        public float Hp { get; set; }
        public float MaxHp { get; set; }
        public bool Dead { get; set; }
        public bool Captured { get; set; }

        public EntityState Copy() => new EntityState
        {
            Id = Id, Kind = Kind, DataTid = DataTid, Root = Root,
            Hp = Hp, MaxHp = MaxHp, Dead = Dead, Captured = Captured
        };
    }

    public sealed class WorldSnapshot
    {
        public long SceneEpoch { get; set; }
        public string SceneKey { get; set; }
        public long Revision { get; set; }
        public double SampleTime { get; set; }
        public EntityState[] Entities { get; set; } = Array.Empty<EntityState>();
    }

    // Complete snapshots are committed atomically after all bounded slices arrive.
    public sealed class WorldSlice
    {
        public long SceneEpoch { get; set; }
        public string SceneKey { get; set; }
        public long Revision { get; set; }
        public double SampleTime { get; set; }
        public int Index { get; set; }
        public int Count { get; set; }
        public int TotalEntities { get; set; }
        public EntityState[] Entities { get; set; } = Array.Empty<EntityState>();
    }

    public static class WorldFrames
    {
        public const int MaxEntities = 4096;
        public const int EntitiesPerSlice = 64;

        public static WorldSlice[] Split(WorldSnapshot snapshot)
        {
            ValidateSnapshot(snapshot);
            int count = SliceCount(snapshot.Entities.Length);
            var slices = new WorldSlice[count];
            for (int i = 0; i < count; i++)
            {
                int length = Math.Min(EntitiesPerSlice, snapshot.Entities.Length - i * EntitiesPerSlice);
                var entities = new EntityState[length];
                for (int j = 0; j < length; j++) entities[j] = snapshot.Entities[i * EntitiesPerSlice + j].Copy();
                slices[i] = new WorldSlice
                {
                    SceneEpoch = snapshot.SceneEpoch, SceneKey = snapshot.SceneKey, Revision = snapshot.Revision,
                    SampleTime = snapshot.SampleTime, Index = i, Count = count,
                    TotalEntities = snapshot.Entities.Length, Entities = entities
                };
            }
            return slices;
        }

        public static void ValidateSnapshot(WorldSnapshot snapshot)
        {
            if (snapshot == null || snapshot.Entities == null || snapshot.Entities.Length > MaxEntities)
                throw new ProtocolException("Invalid world entity count.");
            ValidateHeader(snapshot.SceneEpoch, snapshot.SceneKey, snapshot.Revision, snapshot.SampleTime);
            var ids = new System.Collections.Generic.HashSet<long>();
            foreach (EntityState entity in snapshot.Entities)
            {
                ValidateEntity(entity);
                if (!ids.Add(entity.Id)) throw new ProtocolException("Duplicate world entity identity.");
            }
        }

        public static void ValidateSlice(WorldSlice slice)
        {
            if (slice == null || slice.TotalEntities < 0 || slice.TotalEntities > MaxEntities ||
                slice.Count != SliceCount(slice.TotalEntities) || slice.Index < 0 || slice.Index >= slice.Count || slice.Entities == null ||
                slice.Entities.Length != Math.Min(EntitiesPerSlice, slice.TotalEntities - slice.Index * EntitiesPerSlice))
                throw new ProtocolException("Invalid world slice boundaries.");
            ValidateHeader(slice.SceneEpoch, slice.SceneKey, slice.Revision, slice.SampleTime);
            var ids = new System.Collections.Generic.HashSet<long>();
            foreach (EntityState entity in slice.Entities)
            {
                ValidateEntity(entity);
                if (!ids.Add(entity.Id)) throw new ProtocolException("Duplicate entity within world slice.");
            }
        }

        public static void ValidateEntity(EntityState entity)
        {
            if (entity == null || entity.Id < 1 || !Enum.IsDefined(typeof(EntityKind), entity.Kind) || entity.DataTid < 1)
                throw new ProtocolException("Invalid world entity identity.");
            PacketCodec.ValidatePose(entity.Root);
            if (!float.IsFinite(entity.Hp) || !float.IsFinite(entity.MaxHp) || entity.Hp < 0 || entity.MaxHp < entity.Hp || entity.MaxHp > 1000000 ||
                (entity.Kind == EntityKind.Fish && entity.MaxHp <= 0) ||
                (entity.Kind == EntityKind.Item && (entity.Hp != 0 || entity.MaxHp != 0 || entity.Captured)))
                throw new ProtocolException("Invalid world entity health.");
        }

        private static void ValidateHeader(long epoch, string scene, long revision, double time)
        {
            if (epoch < 1 || revision < 1 || !double.IsFinite(time) || time < 0 || time > 100000000)
                throw new ProtocolException("Invalid world snapshot version or clock.");
            PacketCodec.RequireText(scene, 160, "world scene key");
        }

        private static int SliceCount(int count) => Math.Max(1, (count + EntitiesPerSlice - 1) / EntitiesPerSlice);
    }
}
