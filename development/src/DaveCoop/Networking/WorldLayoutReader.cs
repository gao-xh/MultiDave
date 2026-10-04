using System;
using System.Collections.Generic;
using DaveCoop.Core.Session;
using DaveCoop.Core.World;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityObject = UnityEngine.Object;

namespace DaveCoop.Networking
{
    // Read-only layout agreement for movement. Does not generate a map, synchronize
    // fish/items, or prove the complete world state is equal.
    internal static class WorldLayoutReader
    {
        private const int MaxPoints = 131072;

        public static SceneDescriptor Read(InGameManager manager)
        {
            if (manager == null || !manager.IsLoadedAll || manager.playerCharacter == null)
                return null;
            string scene = manager.playerCharacter.gameObject.scene.name;
            var entries = new List<string>();
            int points = 0;
            int solidCount = 0;
            foreach (Collider2D collider in UnityObject.FindObjectsOfType<Collider2D>())
            {
                if (collider == null || !collider.enabled || !collider.gameObject.activeInHierarchy || collider.isTrigger) continue;
                if (collider.gameObject.scene.name == "DontDestroyOnLoad") continue;
                Rigidbody2D body = collider.attachedRigidbody;
                if (body != null && body.bodyType != RigidbodyType2D.Static) continue;
                if (collider.GetComponentInParent<BaseCharacter>() != null) continue;
                var hash = new CanonicalHash("static-collider-v1").Add(collider.gameObject.scene.name).Add(Path(collider.transform));
                Matrix(hash, collider.transform.localToWorldMatrix);
                Vector(hash, collider.offset);
                if (!Geometry(collider, hash, ref points))
                    throw new InvalidOperationException("Unsupported static collider: " + collider.GetIl2CppType().FullName);
                entries.Add(hash.Finish()); solidCount++;
                if (entries.Count > LayoutFingerprint.MaxEntries) throw new InvalidOperationException("Too many static layout objects.");
            }
            if (solidCount == 0) throw new InvalidOperationException("No static dive geometry available for layout agreement.");
            foreach (DynamicIngameNodeLoader node in UnityObject.FindObjectsOfType<DynamicIngameNodeLoader>())
            {
                if (node == null || !node.gameObject.activeInHierarchy) continue;
                string address = node.addressablePrefabName;
                if (string.IsNullOrWhiteSpace(address)) return null; // Layout selection still loading.
                var hash = new CanonicalHash("selected-node-v1").Add(node.gameObject.scene.name).Add(Path(node.transform)).Add(address);
                Matrix(hash, node.transform.localToWorldMatrix);
                entries.Add(hash.Finish());
            }
            // Loaded scene names are included, while process-local handles/IDs are excluded.
            for (int i = 0; i < SceneManager.sceneCount; i++)
            {
                var loaded = SceneManager.GetSceneAt(i);
                if (loaded.isLoaded) entries.Add(new CanonicalHash("loaded-scene-v1").Add(loaded.name).Finish());
            }
            return new SceneDescriptor(scene, LayoutFingerprint.Create(scene, entries));
        }

        private static bool Geometry(Collider2D collider, CanonicalHash hash, ref int points)
        {
            var box = collider.TryCast<BoxCollider2D>();
            if (box != null) { hash.Add("box").Add(box.edgeRadius); Vector(hash, box.size); return true; }
            var circle = collider.TryCast<CircleCollider2D>();
            if (circle != null) { hash.Add("circle").Add(circle.radius); return true; }
            var capsule = collider.TryCast<CapsuleCollider2D>();
            if (capsule != null) { hash.Add("capsule").Add((int)capsule.direction); Vector(hash, capsule.size); return true; }
            var polygon = collider.TryCast<PolygonCollider2D>();
            if (polygon != null)
            {
                hash.Add("polygon").Add(polygon.pathCount);
                if (polygon.pathCount < 0 || polygon.pathCount > 4096) throw new InvalidOperationException("Invalid polygon path count.");
                for (int i = 0; i < polygon.pathCount; i++) Points(hash, polygon.GetPath(i), ref points);
                return true;
            }
            var edge = collider.TryCast<EdgeCollider2D>();
            if (edge != null) { hash.Add("edge").Add(edge.edgeRadius); Points(hash, edge.points, ref points); return true; }
            var composite = collider.TryCast<CompositeCollider2D>();
            if (composite != null)
            {
                hash.Add("composite").Add((int)composite.geometryType).Add(composite.edgeRadius).Add(composite.pathCount);
                if (composite.pathCount < 0 || composite.pathCount > 4096) throw new InvalidOperationException("Invalid composite path count.");
                for (int i = 0; i < composite.pathCount; i++)
                {
                    int count = composite.GetPathPointCount(i);
                    CheckPoints(count, points);
                    var path = new Il2CppStructArray<Vector2>(count);
                    if (composite.GetPath(i, path) != count) throw new InvalidOperationException("Composite geometry changed while reading.");
                    Points(hash, path, ref points);
                }
                return true;
            }
            return false;
        }

        private static void Points(CanonicalHash hash, Il2CppStructArray<Vector2> path, ref int count)
        {
            if (path == null) throw new InvalidOperationException("Missing collider path.");
            CheckPoints(path.Length, count); count += path.Length;
            hash.Add(path.Length);
            for (int i = 0; i < path.Length; i++) Vector(hash, path[i]);
        }

        private static void CheckPoints(int added, int count)
        {
            if (added < 0 || added > MaxPoints - count) throw new InvalidOperationException("Static layout geometry limit exceeded.");
        }

        private static void Vector(CanonicalHash hash, Vector2 value) { hash.Add(value.x).Add(value.y); }

        private static void Matrix(CanonicalHash hash, Matrix4x4 matrix)
        {
            for (int row = 0; row < 4; row++) for (int column = 0; column < 4; column++) hash.Add(matrix[row, column]);
        }

        private static string Path(Transform node)
        {
            var names = new List<string>();
            for (int depth = 0; node != null && depth < 64; depth++, node = node.parent) names.Add(node.name);
            names.Reverse(); return string.Join("/", names);
        }
    }
}
