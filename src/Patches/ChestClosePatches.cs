using System;
using System.Collections;
using System.Reflection;
using HarmonyLib;

namespace CategorySorter
{
    /// <summary>
    /// Registriert den besten verfuegbaren Hook fuer das Schliessen einer Lagerkiste.
    /// V3 nutzt TEFeatureStorage.OnUnlockedServer; aeltere Builds hatten GameManager.TEUnlockServer.
    /// </summary>
    public static class ChestClosePatches
    {
        private static readonly Type[] StorageUnlockParameters = { typeof(int), typeof(ushort) };
        private static readonly Type[] LegacyUnlockParameters = { typeof(int), typeof(Vector3i), typeof(int), typeof(bool) };

        public static bool Apply(Harmony harmony)
        {
            var storageUnlock = AccessTools.Method(typeof(TEFeatureStorage), "OnUnlockedServer", StorageUnlockParameters);
            if (storageUnlock != null)
            {
                harmony.Patch(
                    storageUnlock,
                    prefix: new HarmonyMethod(AccessTools.Method(
                        typeof(TEFeatureStorage_OnUnlockedServer_Patch),
                        nameof(TEFeatureStorage_OnUnlockedServer_Patch.Prefix))));

                Log.Out("[" + ModApi.ModName + "] patched TEFeatureStorage.OnUnlockedServer");
                return true;
            }

            var legacyUnlock = AccessTools.Method(typeof(GameManager), "TEUnlockServer", LegacyUnlockParameters)
                ?? AccessTools.Method(typeof(GameManager), "TEUnlockServer");
            if (legacyUnlock != null)
            {
                harmony.Patch(
                    legacyUnlock,
                    prefix: new HarmonyMethod(AccessTools.Method(
                        typeof(GameManager_TEUnlockServer_Patch),
                        nameof(GameManager_TEUnlockServer_Patch.Prefix))));

                Log.Out("[" + ModApi.ModName + "] patched GameManager.TEUnlockServer");
                return true;
            }

            return false;
        }

        public static void SortClosedChest(TileEntity chest, Vector3i blockPos, int playerId)
        {
            try
            {
                if (!SingletonMonoBehaviour<ConnectionManager>.Instance.IsServer) return;
                if (chest == null) return;
                if (!GameManager.Instance.World.Players.dict.TryGetValue(playerId, out var player)) return;

                Sorter.DoSortingOut(chest, blockPos, player);
            }
            catch (Exception e)
            {
                Log.Error("[" + ModApi.ModName + "] " + e);
            }
        }
    }

    public static class TEFeatureStorage_OnUnlockedServer_Patch
    {
        public static void Prefix(TEFeatureStorage __instance, int _unlockingPlayerId, ushort _channel)
        {
            var chest = __instance != null ? __instance.Parent : null;
            if (chest == null) return;

            ChestClosePatches.SortClosedChest(chest, chest.ToWorldPos(), _unlockingPlayerId);
        }
    }

    public static class GameManager_TEUnlockServer_Patch
    {
        private static readonly FieldInfo LockedTileEntitiesField =
            AccessTools.Field(typeof(GameManager), "lockedTileEntities");

        public static void Prefix(int _clrIdx, Vector3i _blockPos, int _lootEntityId, bool _allowContainerDestroy)
        {
            try
            {
                TileEntity chest;
                int lockerId;
                if (!TryFindLockedChest(_blockPos, _lootEntityId, out chest, out lockerId)) return;

                ChestClosePatches.SortClosedChest(chest, _blockPos, lockerId);
            }
            catch (Exception e)
            {
                Log.Error("[" + ModApi.ModName + "] " + e);
            }
        }

        private static bool TryFindLockedChest(Vector3i blockPos, int lootEntityId, out TileEntity chest, out int lockerId)
        {
            chest = null;
            lockerId = -1;

            if (lootEntityId == -1)
                chest = GameManager.Instance.World.GetTileEntity(blockPos);

            var entries = LockedTileEntitiesField != null
                ? LockedTileEntitiesField.GetValue(GameManager.Instance) as IEnumerable
                : null;
            if (entries == null) return false;

            foreach (var entry in entries)
            {
                object key;
                int value;
                if (!TryReadEntry(entry, out key, out value)) continue;

                var tileEntity = key as TileEntity;
                if (tileEntity == null) continue;

                if (lootEntityId == -1)
                {
                    if (!tileEntity.ToWorldPos().Equals(blockPos)) continue;
                }
                else
                {
                    int entityId;
                    if (!TryGetEntityId(key, out entityId) || entityId != lootEntityId) continue;
                }

                chest = tileEntity;
                lockerId = value;
                return chest != null && lockerId != -1;
            }

            return false;
        }

        private static bool TryReadEntry(object entry, out object key, out int value)
        {
            key = null;
            value = -1;

            if (entry == null) return false;
            var type = entry.GetType();
            var keyProperty = AccessTools.Property(type, "Key");
            var valueProperty = AccessTools.Property(type, "Value");
            if (keyProperty == null || valueProperty == null) return false;

            key = keyProperty.GetValue(entry, null);
            var rawValue = valueProperty.GetValue(entry, null);
            if (!(rawValue is int)) return false;

            value = (int)rawValue;
            return true;
        }

        private static bool TryGetEntityId(object target, out int entityId)
        {
            entityId = -1;
            if (target == null) return false;

            var type = target.GetType();
            var property = AccessTools.Property(type, "EntityId");
            if (property != null)
            {
                var value = property.GetValue(target, null);
                if (value is int)
                {
                    entityId = (int)value;
                    return true;
                }
            }

            var field = AccessTools.Field(type, "EntityId");
            if (field != null)
            {
                var value = field.GetValue(target);
                if (value is int)
                {
                    entityId = (int)value;
                    return true;
                }
            }

            return false;
        }
    }
}
