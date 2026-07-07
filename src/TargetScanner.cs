using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;

namespace CategorySorter
{
    /// <summary>
    /// Findet moegliche Ziel-Kisten rund um die Sortierkiste. Uebernommen aus dem bewaehrten
    /// Muster von Relvl/7DTD-SortingChest: 3x3-Chunk-Scan, Radius-Filter, Ausschluss von
    /// gerade geoeffneten / unberuehrten / sich selbst.
    /// </summary>
    public static class TargetScanner
    {
        private const ushort StorageLockChannel = 0;

        private static readonly Type LockManagerType = AccessTools.TypeByName("LockManager");
        private static readonly PropertyInfo LockManagerInstanceProperty =
            LockManagerType != null ? AccessTools.Property(LockManagerType, "Instance") : null;
        private static readonly MethodInfo LockManagerIsLockedServerMethod =
            LockManagerType != null
                ? AccessTools.Method(LockManagerType, "IsLockedServer", new[] { typeof(ILockTarget), typeof(ushort) })
                : null;
        private static readonly MethodInfo LegacyGetEntityIdForLockedTileEntityMethod =
            AccessTools.Method(typeof(GameManager), "GetEntityIDForLockedTileEntity", new[] { typeof(TileEntity) });

        /// <summary>Ziel-TileEntities, nach Distanz zur Sortierkiste aufsteigend sortiert.</summary>
        public static List<TileEntity> GetPossibleTargets(Vector3i blockPos)
        {
            var possibleTargets = new Dictionary<Vector3i, TileEntity>();
            var chunkX = World.toChunkXZ(blockPos.x);
            var chunkZ = World.toChunkXZ(blockPos.z);

            var distance = Config.MaxDistance < 0 ? 0 : Math.Pow(Config.MaxDistance, 2);

            for (var offX = -1; offX < 2; offX++)
            {
                for (var offZ = -1; offZ < 2; offZ++)
                {
                    if (!(GameManager.Instance.World.GetChunkSync(chunkX + offX, chunkZ + offZ) is Chunk chunk))
                        continue;

                    foreach (var entry in chunk.GetTileEntities().dict)
                    {
                        var targetPos = chunk.ToWorldPos(entry.Key);
                        if (CheckTileEntityTarget(entry, targetPos, blockPos, distance))
                            possibleTargets[targetPos] = entry.Value;
                    }
                }
            }

            return possibleTargets
                .OrderBy(kv => (blockPos.ToVector3() - kv.Key).sqrMagnitude)
                .Select(kv => kv.Value)
                .ToList();
        }

        private static bool CheckTileEntityTarget(
            KeyValuePair<Vector3i, TileEntity> entry, Vector3i targetPos, Vector3i blockPos, double distance)
        {
            // sich selbst ueberspringen
            if (targetPos.Equals(blockPos)) return false;

            // nur Kisten-aehnliche Typen
            if (!Config.AvailableTargetTypes.Contains(entry.Value.GetTileEntityType())) return false;

            // andere Sortierkisten ueberspringen
            if (entry.Value is TileEntityComposite targetComposite)
            {
                var targetSignable = targetComposite.GetFeature<TEFeatureSignable>();
                var tname = targetSignable != null && targetSignable.signText != null ? targetSignable.signText.Text : null;
                if (TagUtil.TagEquals(tname, Config.SortTag)) return false;
            }

            // nicht in unberuehrte Loot-Container schieben (z. B. noch nicht gepluenderte Welt-Container)
            ITileEntityLootable lootable;
            if (entry.Value.TryGetSelfOrFeature<ITileEntityLootable>(out lootable)
                && !lootable.bPlayerStorage
                && !lootable.bTouched)
                return false;

            // zu weit entfernt
            if (distance > 0)
            {
                var distanceSq = (blockPos.ToVector3() - targetPos).sqrMagnitude;
                if (distanceSq > distance) return false;
            }

            // gerade von einem Spieler geoeffnet
            if (IsLockedByPlayer(entry.Value)) return false;

            return true;
        }

        private static bool IsLockedByPlayer(TileEntity tileEntity)
        {
            if (tileEntity == null) return false;
            if (IsLockedWithModernLockManager(tileEntity)) return true;
            if (IsLockedWithLegacyGameManager(tileEntity)) return true;
            return tileEntity.IsUserAccessing();
        }

        private static bool IsLockedWithModernLockManager(TileEntity tileEntity)
        {
            if (LockManagerInstanceProperty == null || LockManagerIsLockedServerMethod == null)
                return false;

            var target = tileEntity as ILockTarget;
            if (target == null)
            {
                var composite = tileEntity as TileEntityComposite;
                if (composite != null)
                    target = composite.GetFeature<TEFeatureStorage>();
            }

            if (target == null) return false;

            var manager = LockManagerInstanceProperty.GetValue(null, null);
            if (manager == null) return false;

            var result = LockManagerIsLockedServerMethod.Invoke(
                manager,
                new object[] { target, StorageLockChannel });
            return result is bool && (bool)result;
        }

        private static bool IsLockedWithLegacyGameManager(TileEntity tileEntity)
        {
            if (LegacyGetEntityIdForLockedTileEntityMethod == null) return false;

            var result = LegacyGetEntityIdForLockedTileEntityMethod.Invoke(
                GameManager.Instance,
                new object[] { tileEntity });
            return result is int && (int)result != -1;
        }
    }
}
