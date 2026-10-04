using System;
using System.Collections.Generic;
using System.Globalization;
using DaveCoop.Core;
using DaveCoop.Core.Protocol;
using DaveCoop.Rendering;
using UnityEngine;
using Pose = DaveCoop.Core.Pose;

namespace DaveCoop.Networking
{
    // One already-existing local harpoon head. No weapon, rope, collider or action
    // is created, and these display slots never identify a native actor/operation.
    internal sealed partial class LocalAvatarCapture
    {
        private const string HarpoonSlotPrefix = "@multidave/harpoon-head/";
        private sealed class HarpoonHead
        {
            public PlayerCharacter Player;
            public InstanceItemInventory Inventory;
            public HarpoonWeaponHandler Handler;
            public HarpoonProjectile Projectile;
            public SpriteRenderer Renderer;
            public Transform Owner;
            public int PlayerId, InventoryId, HandlerId, ProjectileId, RendererId, OwnerId;
        }

        private SpriteRenderer _harpoonTemplate;
        private string _harpoonTemplateKey;
        private int _harpoonTemplateSpriteId;
        private int _harpoonBoundHandler, _harpoonBoundProjectile, _harpoonBoundRenderer;
        private long _harpoonVisualRevision;
        public int HarpoonVisualParts { get; private set; }
        public int HarpoonVisualVisibleParts { get; private set; }
        public int HarpoonVisualDuplicateParts { get; private set; }
        public int HarpoonVisualUnkeyedParts { get; private set; }
        public long HarpoonVisualReadErrors { get; private set; }
        public long HarpoonVisualCapacitySkips { get; private set; }
        public string HarpoonVisualStatus { get; private set; } = "NotSampled";

        public static bool IsHarpoonSlot(string slot)
        {
            if (slot == null || !slot.StartsWith(HarpoonSlotPrefix, StringComparison.Ordinal)) return false;
            // Body slots always end in #renderer-N. The reserved slot is only a
            // positive local display revision, never a Unity instance ID.
            string revision = slot.Substring(HarpoonSlotPrefix.Length);
            return long.TryParse(revision, NumberStyles.None, CultureInfo.InvariantCulture, out long value) && value > 0;
        }

        private List<SpriteRenderer> CollectBodySprites()
        {
            var result = new List<SpriteRenderer>();
            var sprites = Player.GetComponentsInChildren<SpriteRenderer>(true);
            int headId = 0;
            try
            {
                HarpoonHead head = ReadOwnHarpoonHead();
                if (head != null && HeadStillOwned(head)) headId = head.RendererId;
            }
            catch (Exception) { HarpoonVisualReadErrors++; }
            foreach (SpriteRenderer sprite in sprites)
                if (sprite != null && (headId == 0 || sprite.GetInstanceID() != headId)) result.Add(sprite);
            return result;
        }

        private void CaptureHarpoonHead(double now, Pose rootPose, Transform origin, Quaternion inverse,
            Vector3 rootScale, List<SpritePartFrame> parts, SpriteCatalog catalog)
        {
            HarpoonVisualParts = 0; HarpoonVisualVisibleParts = 0;
            HarpoonVisualDuplicateParts = 0; HarpoonVisualUnkeyedParts = 0;
            _harpoonTemplate = null; _harpoonTemplateKey = null; _harpoonTemplateSpriteId = 0;
            try
            {
                HarpoonHead head = ReadOwnHarpoonHead();
                if (head == null) { OmitHarpoonHead("UnavailableOwnerOrHead"); return; }
                foreach (Part part in _parts)
                    if (part.Source != null && part.Source.GetInstanceID() == head.RendererId)
                    {
                        HarpoonVisualDuplicateParts = 1; OmitHarpoonHead("AlreadyInBodyFrame"); return;
                    }
                if (parts.Count >= PacketCodec.MaxParts)
                {
                    HarpoonVisualCapacitySkips++; OmitHarpoonHead("FramePartLimit"); return;
                }
                Sprite sprite = head.Renderer.sprite;
                if (sprite == null) { OmitHarpoonHead("NoSprite"); return; }
                int spriteId = sprite.GetInstanceID();
                bool newBinding = _harpoonBoundHandler != head.HandlerId || _harpoonBoundProjectile != head.ProjectileId ||
                    _harpoonBoundRenderer != head.RendererId;
                if (newBinding && _harpoonVisualRevision == long.MaxValue) { OmitHarpoonHead("DisplayRevisionLimit"); return; }
                long revision = newBinding ? _harpoonVisualRevision + 1 : _harpoonVisualRevision;
                Color color = head.Renderer.color; Vector3 scale = head.Renderer.transform.lossyScale;
                bool visible = head.Renderer.enabled && head.Renderer.gameObject.activeInHierarchy;
                var candidate = new SpritePartFrame
                {
                    Slot = HarpoonSlotPrefix + revision.ToString(CultureInfo.InvariantCulture),
                    Pose = PoseOf(origin.InverseTransformPoint(head.Renderer.transform.position),
                        inverse * head.Renderer.transform.rotation,
                        new Vector3(Divide(scale.x, rootScale.x), Divide(scale.y, rootScale.y), Divide(scale.z, rootScale.z))),
                    Color = new System.Numerics.Vector4(color.r, color.g, color.b, color.a),
                    FlipX = head.Renderer.flipX, FlipY = head.Renderer.flipY, Layer = head.Renderer.gameObject.layer,
                    SortingLayer = head.Renderer.sortingLayerID, SortingOrder = head.Renderer.sortingOrder
                };
                if (!HeadStillOwned(head) || head.Renderer.sprite == null || head.Renderer.sprite.GetInstanceID() != spriteId)
                { OmitHarpoonHead("OwnerChangedDuringSample"); return; }
                // Only after the owned scalar sample passes its identity check may
                // a sprite key be registered. A later mismatch still omits this part.
                string key = catalog.Register(sprite);
                if (key == null) { HarpoonVisualUnkeyedParts = visible ? 1 : 0; OmitHarpoonHead("UnresolvedSprite"); return; }
                candidate.SpriteKey = key; candidate.Visible = visible;
                PacketCodec.ValidateFrame(new PlayerFrame
                {
                    PlayerId = 1, SceneEpoch = 1, SceneKey = head.Player.gameObject.scene.name,
                    SampleTime = now, Root = rootPose, Parts = new[] { candidate }
                });
                if (!HeadStillOwned(head) || head.Renderer.sprite == null || head.Renderer.sprite.GetInstanceID() != spriteId)
                { OmitHarpoonHead("OwnerChangedAfterSpriteKey"); return; }
                // The DTO/key/template transaction is now complete; only CLR state
                // is changed below. A missing frame never reuses a previous head.
                parts.Add(candidate); _harpoonVisualRevision = revision;
                _harpoonBoundHandler = head.HandlerId; _harpoonBoundProjectile = head.ProjectileId;
                _harpoonBoundRenderer = head.RendererId;
                _harpoonTemplate = head.Renderer; _harpoonTemplateKey = key; _harpoonTemplateSpriteId = spriteId;
                HarpoonVisualParts = 1; HarpoonVisualVisibleParts = visible ? 1 : 0;
                HarpoonVisualStatus = visible ? "Captured" : "Hidden";
            }
            catch (Exception)
            {
                HarpoonVisualReadErrors++; OmitHarpoonHead("SampleError");
            }
        }

        private HarpoonHead ReadOwnHarpoonHead()
        {
            if (!IsAvailable || Manager._playerCharacter_k__BackingField != Player) return null;
            PlayerCharacter player = Player;
            // These generated properties are direct NativeFieldInfoPtr proxies.
            // CurrentInstanceItemInventory is a business getter and is not used.
            InstanceItemInventory inventory = player.m_InstanceItemInven;
            if (inventory == null || inventory.gameObject.scene.handle != SceneHandle) return null;
            HarpoonWeaponHandler handler = inventory.harpoonHandler;
            if (handler == null || handler.gameObject.scene.handle != SceneHandle) return null;
            HarpoonProjectile projectile = handler.harpoonProjectile;
            SpriteRenderer renderer = handler.projectileRenderer;
            if (projectile == null || renderer == null || projectile.gameObject.scene.handle != SceneHandle ||
                renderer.gameObject.scene.handle != SceneHandle || projectile.m_HarpoonHandler != handler) return null;
            Transform owner = projectile._Owner_k__BackingField;
            if (owner == null || !(owner == player.transform || owner.IsChildOf(player.transform)) ||
                !(renderer.transform == projectile.transform || renderer.transform.IsChildOf(projectile.transform))) return null;
            return new HarpoonHead
            {
                Player = player, Inventory = inventory, Handler = handler, Projectile = projectile, Renderer = renderer, Owner = owner,
                PlayerId = player.GetInstanceID(), InventoryId = inventory.GetInstanceID(), HandlerId = handler.GetInstanceID(),
                ProjectileId = projectile.GetInstanceID(), RendererId = renderer.GetInstanceID(), OwnerId = owner.GetInstanceID()
            };
        }

        private bool HeadStillOwned(HarpoonHead head)
        {
            if (!IsAvailable || Manager._playerCharacter_k__BackingField != head.Player || Player != head.Player || PlayerId != head.PlayerId || head.Inventory == null ||
                head.Handler == null || head.Projectile == null || head.Renderer == null || head.Owner == null) return false;
            return Player.m_InstanceItemInven == head.Inventory && head.Inventory.GetInstanceID() == head.InventoryId &&
                head.Inventory.harpoonHandler == head.Handler && head.Handler.GetInstanceID() == head.HandlerId &&
                head.Handler.harpoonProjectile == head.Projectile && head.Handler.projectileRenderer == head.Renderer &&
                head.Projectile.GetInstanceID() == head.ProjectileId && head.Renderer.GetInstanceID() == head.RendererId &&
                head.Projectile.m_HarpoonHandler == head.Handler && head.Projectile._Owner_k__BackingField == head.Owner &&
                head.Owner.GetInstanceID() == head.OwnerId && head.Inventory.gameObject.scene.handle == SceneHandle &&
                head.Handler.gameObject.scene.handle == SceneHandle && head.Projectile.gameObject.scene.handle == SceneHandle &&
                head.Renderer.gameObject.scene.handle == SceneHandle &&
                (head.Owner == Player.transform || head.Owner.IsChildOf(Player.transform)) &&
                (head.Renderer.transform == head.Projectile.transform || head.Renderer.transform.IsChildOf(head.Projectile.transform));
        }

        private SpriteRenderer HarpoonTemplate(string spriteKey)
        {
            try
            {
                if (_harpoonTemplate == null || spriteKey == null || !string.Equals(spriteKey, _harpoonTemplateKey, StringComparison.Ordinal)) return null;
                HarpoonHead head = ReadOwnHarpoonHead();
                if (head == null || head.Renderer != _harpoonTemplate || head.RendererId != _harpoonBoundRenderer ||
                    !HeadStillOwned(head) || head.Renderer.sprite == null || head.Renderer.sprite.GetInstanceID() != _harpoonTemplateSpriteId) return null;
                return head.Renderer;
            }
            catch (Exception) { HarpoonVisualReadErrors++; OmitHarpoonHead("TemplateError"); return null; }
        }

        private void OmitHarpoonHead(string reason)
        {
            _harpoonTemplate = null; _harpoonTemplateKey = null; _harpoonTemplateSpriteId = 0;
            _harpoonBoundHandler = 0; _harpoonBoundProjectile = 0; _harpoonBoundRenderer = 0;
            HarpoonVisualStatus = reason;
        }

        private void ClearHarpoonVisual()
        {
            OmitHarpoonHead("Cleared"); HarpoonVisualParts = 0; HarpoonVisualVisibleParts = 0;
            HarpoonVisualDuplicateParts = 0; HarpoonVisualUnkeyedParts = 0;
            // Keep the local slot revision and cumulative diagnostic counters.
        }
    }
}
