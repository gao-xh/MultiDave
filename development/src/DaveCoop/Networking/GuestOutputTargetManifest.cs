using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using DR.Save;

namespace DaveCoop.Networking
{
    // Exact declarations frozen from this build's offline metadata. Updating
    // the game requires a reviewed reinspection, not name-only fallback.
    // Abstract service entries are not detour targets; closed generic native
    // aliases and complete persistent writer coverage remain unverified.
    internal static class GuestOutputTargetManifest
    {
        public const int ExpectedTargetCount = 194;
        private static readonly string[] Declarations =
        {
            "Assembly-CSharp\tGDKSaveLoadModule\tSaveData\tfalse\tSystem.Void\tSystem.String\tIl2CppInterop.Runtime.InteropTypes.Arrays.Il2CppStructArray`1<System.Byte>",
            "Assembly-CSharp\tGDKSaveLoadModule\tDeleteData\tfalse\tSystem.Void\tSystem.String",
            "Assembly-CSharp\tGDKSaveLoadModule\tDeleteFiles\tfalse\tSystem.Void\tIl2CppInterop.Runtime.InteropTypes.Arrays.Il2CppStringArray",
            "Assembly-CSharp\tSteamAchievements\tSetStatById\tfalse\tSystem.Void\tSystem.String\tSystem.Int32",
            "Assembly-CSharp\tSteamAchievements\tUnlockAchievement\tfalse\tSystem.Void\tSystem.String",
            "Assembly-CSharp\tSteamAchievements\tUnlockAchievementWithStat\tfalse\tSystem.Void\tSystem.String\tSystem.String\tSystem.Int32",
            "Assembly-CSharp\tSteamAchievements\tUpdateAchievementValueWithStat\tfalse\tSystem.Void\tDR.Achievement\tSystem.Int32\tSystem.Boolean",
            "Assembly-CSharp\tSteamAchievements\tLockAchievement\tfalse\tSystem.Void\tSystem.String",
            "Assembly-CSharp\tSteamAchievements\tResetAll\tfalse\tSystem.Void",
            "Assembly-CSharp\tSteamAchievements\tOnDREvent\tfalse\tSystem.Void\tResetEvent",
            "Assembly-CSharp\tSteamAchievements\tOnDREvent\tfalse\tSystem.Void\tAchievementUpdateDataEvent",
            "Assembly-CSharp\tSteamAchievements\tUnlockProgressSyncFromSave\tfalse\tSystem.Void",
            "Assembly-CSharp\tSteamAchievements\tCompletedMission\tfalse\tSystem.Void\tMissionData\tSystem.Int32",
            "Assembly-CSharp\tSteamAchievements\tUpdateMissionCondition\tfalse\tSystem.Void\tSystem.Int32",
            "Assembly-CSharp\tDR.Save.SaveSystem\tSaveAllData\tfalse\tSystem.Void",
            "Assembly-CSharp\tDR.Save.SaveSystem\tSaveGameDataInSlot\tfalse\tSystem.Boolean\tSystem.Int32\tSystem.Boolean",
            "Assembly-CSharp\tDR.Save.SaveSystem\tSaveGameDataInSlot\tfalse\tSystem.Boolean\tSystem.Int32\tSystem.Boolean\tDR.Save.SaveSlotType",
            "Assembly-CSharp\tDR.Save.SaveSystem\tLoadGameDataFromSlot\tfalse\tSystem.Boolean\tSystem.Int32\tDR.Save.SaveSlotType",
            "Assembly-CSharp\tDR.Save.SaveSystem\tLoadGameDataFromJson\tfalse\tSystem.Boolean\tSystem.String",
            "Assembly-CSharp\tDR.Save.SaveSystem\tTrySaveGameData\tfalse\tSystem.Boolean",
            "Assembly-CSharp\tDR.Save.SaveSystem\tSaveGameData\tfalse\tSystem.Boolean",
            "Assembly-CSharp\tDR.Save.SaveSystem\tSavePhotoData\tfalse\tSystem.Void",
            "Assembly-CSharp\tDR.Save.SaveSystem\tDeleteGameData\tfalse\tSystem.Void",
            "Assembly-CSharp\tDR.Save.SaveSystem\tLoadGame\tfalse\tSystem.Void",
            "Assembly-CSharp\tDR.Save.SaveSystem\tResetAfterCloudLoad\tfalse\tSystem.Void",
            "Assembly-CSharp\tDR.Save.SaveSystem\tReloadData\tfalse\tSystem.Void",
            "Assembly-CSharp\tDR.Save.SaveSystem\tLoadGameOnInit\tfalse\tSystem.Void",
            "Assembly-CSharp\tDR.Save.SaveSystem\tCheckSaveVersion\tfalse\tSystem.Void",
            "Assembly-CSharp\tDR.Save.SaveSystem\tLoadAllData\tfalse\tSystem.Void",
            "Assembly-CSharp\tDR.Save.SaveSystem\tTestSaveGameData\tfalse\tSystem.Void",
            "Assembly-CSharp\tDR.Save.SaveSystem\tTestLoadGameData\tfalse\tSystem.Void",
            "Assembly-CSharp\tDR.Save.SaveSystem\tTestSavePhotoData\tfalse\tSystem.Void",
            "Assembly-CSharp\tDR.Save.SaveSystemGameDataManager\tReset\tfalse\tSystem.Void",
            "Assembly-CSharp\tDR.Save.SaveSystemGameDataManager\tSaveData\tfalse\tSystem.Void\tSystem.Boolean",
            "Assembly-CSharp\tDR.Save.SaveSystemGameDataManager\tCreateManagedData\tfalse\tSystem.Void",
            "Assembly-CSharp\tDR.Save.SaveSystemGameDataManager\tOnLoadData\tfalse\tSystem.Void",
            "Assembly-CSharp\tDR.Save.SaveSystemGameDataManager\tCheckSaveVersion\tfalse\tSystem.Void",
            "Assembly-CSharp\tDR.Save.SaveSystemGameDataManager\tCopyDemoSaveFiles\tfalse\tSystem.Void",
            "Assembly-CSharp\tDR.Save.SaveSystemGameDataManager\tDeleteSaveFile\tfalse\tSystem.Void",
            "Assembly-CSharp\tDR.Save.SaveSystemPhotoDataManager\tSaveData\tfalse\tSystem.Void\tSystem.Boolean",
            "Assembly-CSharp\tDR.Save.SaveSystemPhotoDataManager\tCreateManagedData\tfalse\tSystem.Void",
            "Assembly-CSharp\tDR.Save.SaveSystemPhotoDataManager\tOnLoadData\tfalse\tSystem.Void",
            "Assembly-CSharp\tDR.Save.SaveSystemPhotoDataManager\tDeleteSaveFile\tfalse\tSystem.Void",
            "Assembly-CSharp\tDR.Save.SaveSystemPlayerDataManager\tCreateManagedData\tfalse\tSystem.Void",
            "Assembly-CSharp\tDR.Save.SaveSystemPlayerDataManager\tLoadData\tfalse\tSystem.Void",
            "Assembly-CSharp\tDR.Save.SaveSystemPlayerDataManager\tOnLoadData\tfalse\tSystem.Void",
            "Assembly-CSharp\tDR.Save.SaveSystemPlayerDataManager\tSetLoadedData\tfalse\tSystem.Void\tDR.Save.SavePlayerData",
            "Assembly-CSharp\tDR.Save.SaveSystemPlayerDataManager\tClearData\tfalse\tSystem.Void\tSystem.Boolean",
            "Assembly-CSharp\tDR.Save.SaveSystemUserOptionManager\tCreateManagedData\tfalse\tSystem.Void",
            "Assembly-CSharp\tDR.Save.SaveSystemUserOptionManager\tOnLoadData\tfalse\tSystem.Void",
            "Assembly-CSharp\tDR.Save.SaveSystemUserOptionManager\tSetMakeBackupEndingSlot\tfalse\tSystem.Void\tSystem.Boolean\tSystem.Boolean",
            "com.rlabrecque.steamworks.net\tSteamworks.SteamRemoteStorage\tFileWrite\ttrue\tSystem.Boolean\tSystem.String\tIl2CppInterop.Runtime.InteropTypes.Arrays.Il2CppStructArray`1<System.Byte>\tSystem.Int32",
            "com.rlabrecque.steamworks.net\tSteamworks.SteamRemoteStorage\tFileWriteAsync\ttrue\tSteamworks.SteamAPICall_t\tSystem.String\tIl2CppInterop.Runtime.InteropTypes.Arrays.Il2CppStructArray`1<System.Byte>\tSystem.UInt32",
            "com.rlabrecque.steamworks.net\tSteamworks.SteamRemoteStorage\tFileForget\ttrue\tSystem.Boolean\tSystem.String",
            "com.rlabrecque.steamworks.net\tSteamworks.SteamRemoteStorage\tFileDelete\ttrue\tSystem.Boolean\tSystem.String",
            "com.rlabrecque.steamworks.net\tSteamworks.SteamRemoteStorage\tFileShare\ttrue\tSteamworks.SteamAPICall_t\tSystem.String",
            "com.rlabrecque.steamworks.net\tSteamworks.SteamRemoteStorage\tSetSyncPlatforms\ttrue\tSystem.Boolean\tSystem.String\tSteamworks.ERemoteStoragePlatform",
            "com.rlabrecque.steamworks.net\tSteamworks.SteamRemoteStorage\tFileWriteStreamOpen\ttrue\tSteamworks.UGCFileWriteStreamHandle_t\tSystem.String",
            "com.rlabrecque.steamworks.net\tSteamworks.SteamRemoteStorage\tFileWriteStreamWriteChunk\ttrue\tSystem.Boolean\tSteamworks.UGCFileWriteStreamHandle_t\tIl2CppInterop.Runtime.InteropTypes.Arrays.Il2CppStructArray`1<System.Byte>\tSystem.Int32",
            "com.rlabrecque.steamworks.net\tSteamworks.SteamRemoteStorage\tFileWriteStreamClose\ttrue\tSystem.Boolean\tSteamworks.UGCFileWriteStreamHandle_t",
            "com.rlabrecque.steamworks.net\tSteamworks.SteamRemoteStorage\tFileWriteStreamCancel\ttrue\tSystem.Boolean\tSteamworks.UGCFileWriteStreamHandle_t",
            "com.rlabrecque.steamworks.net\tSteamworks.SteamRemoteStorage\tBeginFileWriteBatch\ttrue\tSystem.Boolean",
            "com.rlabrecque.steamworks.net\tSteamworks.SteamRemoteStorage\tEndFileWriteBatch\ttrue\tSystem.Boolean",
            "com.rlabrecque.steamworks.net\tSteamworks.SteamUserStats\tSetStat\ttrue\tSystem.Boolean\tSystem.String\tSystem.Int32",
            "com.rlabrecque.steamworks.net\tSteamworks.SteamUserStats\tSetStat\ttrue\tSystem.Boolean\tSystem.String\tSystem.Single",
            "com.rlabrecque.steamworks.net\tSteamworks.SteamUserStats\tUpdateAvgRateStat\ttrue\tSystem.Boolean\tSystem.String\tSystem.Single\tSystem.Double",
            "com.rlabrecque.steamworks.net\tSteamworks.SteamUserStats\tSetAchievement\ttrue\tSystem.Boolean\tSystem.String",
            "com.rlabrecque.steamworks.net\tSteamworks.SteamUserStats\tClearAchievement\ttrue\tSystem.Boolean\tSystem.String",
            "com.rlabrecque.steamworks.net\tSteamworks.SteamUserStats\tStoreStats\ttrue\tSystem.Boolean",
            "com.rlabrecque.steamworks.net\tSteamworks.SteamUserStats\tIndicateAchievementProgress\ttrue\tSystem.Boolean\tSystem.String\tSystem.UInt32\tSystem.UInt32",
            "com.rlabrecque.steamworks.net\tSteamworks.SteamUserStats\tResetAllStats\ttrue\tSystem.Boolean\tSystem.Boolean",
            "com.rlabrecque.steamworks.net\tSteamworks.NativeMethods\tISteamRemoteStorage_FileWrite\ttrue\tSystem.Boolean\tSystem.IntPtr\tSteamworks.InteropHelp/UTF8StringHandle\tIl2CppInterop.Runtime.InteropTypes.Arrays.Il2CppStructArray`1<System.Byte>\tSystem.Int32",
            "com.rlabrecque.steamworks.net\tSteamworks.NativeMethods\tISteamRemoteStorage_FileWriteAsync\ttrue\tSystem.UInt64\tSystem.IntPtr\tSteamworks.InteropHelp/UTF8StringHandle\tIl2CppInterop.Runtime.InteropTypes.Arrays.Il2CppStructArray`1<System.Byte>\tSystem.UInt32",
            "com.rlabrecque.steamworks.net\tSteamworks.NativeMethods\tISteamRemoteStorage_FileForget\ttrue\tSystem.Boolean\tSystem.IntPtr\tSteamworks.InteropHelp/UTF8StringHandle",
            "com.rlabrecque.steamworks.net\tSteamworks.NativeMethods\tISteamRemoteStorage_FileDelete\ttrue\tSystem.Boolean\tSystem.IntPtr\tSteamworks.InteropHelp/UTF8StringHandle",
            "com.rlabrecque.steamworks.net\tSteamworks.NativeMethods\tISteamRemoteStorage_FileShare\ttrue\tSystem.UInt64\tSystem.IntPtr\tSteamworks.InteropHelp/UTF8StringHandle",
            "com.rlabrecque.steamworks.net\tSteamworks.NativeMethods\tISteamRemoteStorage_SetSyncPlatforms\ttrue\tSystem.Boolean\tSystem.IntPtr\tSteamworks.InteropHelp/UTF8StringHandle\tSteamworks.ERemoteStoragePlatform",
            "com.rlabrecque.steamworks.net\tSteamworks.NativeMethods\tISteamRemoteStorage_FileWriteStreamOpen\ttrue\tSystem.UInt64\tSystem.IntPtr\tSteamworks.InteropHelp/UTF8StringHandle",
            "com.rlabrecque.steamworks.net\tSteamworks.NativeMethods\tISteamRemoteStorage_FileWriteStreamWriteChunk\ttrue\tSystem.Boolean\tSystem.IntPtr\tSteamworks.UGCFileWriteStreamHandle_t\tIl2CppInterop.Runtime.InteropTypes.Arrays.Il2CppStructArray`1<System.Byte>\tSystem.Int32",
            "com.rlabrecque.steamworks.net\tSteamworks.NativeMethods\tISteamRemoteStorage_FileWriteStreamClose\ttrue\tSystem.Boolean\tSystem.IntPtr\tSteamworks.UGCFileWriteStreamHandle_t",
            "com.rlabrecque.steamworks.net\tSteamworks.NativeMethods\tISteamRemoteStorage_FileWriteStreamCancel\ttrue\tSystem.Boolean\tSystem.IntPtr\tSteamworks.UGCFileWriteStreamHandle_t",
            "com.rlabrecque.steamworks.net\tSteamworks.NativeMethods\tISteamRemoteStorage_BeginFileWriteBatch\ttrue\tSystem.Boolean\tSystem.IntPtr",
            "com.rlabrecque.steamworks.net\tSteamworks.NativeMethods\tISteamRemoteStorage_EndFileWriteBatch\ttrue\tSystem.Boolean\tSystem.IntPtr",
            "com.rlabrecque.steamworks.net\tSteamworks.NativeMethods\tISteamUserStats_SetStatInt32\ttrue\tSystem.Boolean\tSystem.IntPtr\tSteamworks.InteropHelp/UTF8StringHandle\tSystem.Int32",
            "com.rlabrecque.steamworks.net\tSteamworks.NativeMethods\tISteamUserStats_SetStatFloat\ttrue\tSystem.Boolean\tSystem.IntPtr\tSteamworks.InteropHelp/UTF8StringHandle\tSystem.Single",
            "com.rlabrecque.steamworks.net\tSteamworks.NativeMethods\tISteamUserStats_UpdateAvgRateStat\ttrue\tSystem.Boolean\tSystem.IntPtr\tSteamworks.InteropHelp/UTF8StringHandle\tSystem.Single\tSystem.Double",
            "com.rlabrecque.steamworks.net\tSteamworks.NativeMethods\tISteamUserStats_SetAchievement\ttrue\tSystem.Boolean\tSystem.IntPtr\tSteamworks.InteropHelp/UTF8StringHandle",
            "com.rlabrecque.steamworks.net\tSteamworks.NativeMethods\tISteamUserStats_ClearAchievement\ttrue\tSystem.Boolean\tSystem.IntPtr\tSteamworks.InteropHelp/UTF8StringHandle",
            "com.rlabrecque.steamworks.net\tSteamworks.NativeMethods\tISteamUserStats_StoreStats\ttrue\tSystem.Boolean\tSystem.IntPtr",
            "com.rlabrecque.steamworks.net\tSteamworks.NativeMethods\tISteamUserStats_IndicateAchievementProgress\ttrue\tSystem.Boolean\tSystem.IntPtr\tSteamworks.InteropHelp/UTF8StringHandle\tSystem.UInt32\tSystem.UInt32",
            "com.rlabrecque.steamworks.net\tSteamworks.NativeMethods\tISteamUserStats_ResetAllStats\ttrue\tSystem.Boolean\tSystem.IntPtr\tSystem.Boolean",
            "UnityEngine.CoreModule\tUnityEngine.PlayerPrefs\tTrySetInt\ttrue\tSystem.Boolean\tSystem.String\tSystem.Int32",
            "UnityEngine.CoreModule\tUnityEngine.PlayerPrefs\tTrySetFloat\ttrue\tSystem.Boolean\tSystem.String\tSystem.Single",
            "UnityEngine.CoreModule\tUnityEngine.PlayerPrefs\tTrySetSetString\ttrue\tSystem.Boolean\tSystem.String\tSystem.String",
            "UnityEngine.CoreModule\tUnityEngine.PlayerPrefs\tSetInt\ttrue\tSystem.Void\tSystem.String\tSystem.Int32",
            "UnityEngine.CoreModule\tUnityEngine.PlayerPrefs\tSetFloat\ttrue\tSystem.Void\tSystem.String\tSystem.Single",
            "UnityEngine.CoreModule\tUnityEngine.PlayerPrefs\tSetString\ttrue\tSystem.Void\tSystem.String\tSystem.String",
            "UnityEngine.CoreModule\tUnityEngine.PlayerPrefs\tDeleteKey\ttrue\tSystem.Void\tSystem.String",
            "UnityEngine.CoreModule\tUnityEngine.PlayerPrefs\tDeleteAll\ttrue\tSystem.Void",
            "UnityEngine.CoreModule\tUnityEngine.PlayerPrefs\tSave\ttrue\tSystem.Void",
            "UnityEngine.CoreModule\tUnityEngine.PlayerPrefs\tTrySetInt_Injected\ttrue\tSystem.Boolean\tUnityEngine.Bindings.ManagedSpanWrapper&\tSystem.Int32",
            "UnityEngine.CoreModule\tUnityEngine.PlayerPrefs\tTrySetFloat_Injected\ttrue\tSystem.Boolean\tUnityEngine.Bindings.ManagedSpanWrapper&\tSystem.Single",
            "UnityEngine.CoreModule\tUnityEngine.PlayerPrefs\tTrySetSetString_Injected\ttrue\tSystem.Boolean\tUnityEngine.Bindings.ManagedSpanWrapper&\tUnityEngine.Bindings.ManagedSpanWrapper&",
            "UnityEngine.CoreModule\tUnityEngine.PlayerPrefs\tDeleteKey_Injected\ttrue\tSystem.Void\tUnityEngine.Bindings.ManagedSpanWrapper&",
            "SaveSystem\tToolbox.SaveSystem.SaveManager\tSave\tfalse\tToolbox.SaveSystem.SaveResult\tIl2CppSystem.Object\tSystem.Int32\tSystem.Boolean",
            "SaveSystem\tToolbox.SaveSystem.SaveManager\tDelete\tfalse\tToolbox.SaveSystem.DeleteResult\tSystem.Int32",
            "Assembly-CSharp\tDR.Save.SaveLoadManagerBase`1<SaveData>\tReset\tfalse\tSystem.Void",
            "Assembly-CSharp\tDR.Save.SaveLoadManagerBase`1<DR.Save.SavePlayerData>\tReset\tfalse\tSystem.Void",
            "Assembly-CSharp\tDR.Save.SaveLoadManagerBase`1<SavePhotoData>\tReset\tfalse\tSystem.Void",
            "Assembly-CSharp\tDR.Save.SaveLoadManagerBase`1<DR.Save.SaveUserOptions>\tReset\tfalse\tSystem.Void",
            "Assembly-CSharp\tDR.Save.SaveLoadManagerBase`1<SaveData>\tReportProgressMissionState\tfalse\tSystem.Void",
            "Assembly-CSharp\tDR.Save.SaveLoadManagerBase`1<DR.Save.SavePlayerData>\tReportProgressMissionState\tfalse\tSystem.Void",
            "Assembly-CSharp\tDR.Save.SaveLoadManagerBase`1<SavePhotoData>\tReportProgressMissionState\tfalse\tSystem.Void",
            "Assembly-CSharp\tDR.Save.SaveLoadManagerBase`1<DR.Save.SaveUserOptions>\tReportProgressMissionState\tfalse\tSystem.Void",
            "Assembly-CSharp\tDR.Save.SaveLoadManagerBase`1<SaveData>\tSaveData\tfalse\tSystem.Void\tSystem.Boolean",
            "Assembly-CSharp\tDR.Save.SaveLoadManagerBase`1<DR.Save.SavePlayerData>\tSaveData\tfalse\tSystem.Void\tSystem.Boolean",
            "Assembly-CSharp\tDR.Save.SaveLoadManagerBase`1<SavePhotoData>\tSaveData\tfalse\tSystem.Void\tSystem.Boolean",
            "Assembly-CSharp\tDR.Save.SaveLoadManagerBase`1<DR.Save.SaveUserOptions>\tSaveData\tfalse\tSystem.Void\tSystem.Boolean",
            "Assembly-CSharp\tDR.Save.SaveLoadManagerBase`1<SaveData>\tCopyFileToCloud\tfalse\tSystem.Void\tSystem.Int32\tSystem.Boolean",
            "Assembly-CSharp\tDR.Save.SaveLoadManagerBase`1<DR.Save.SavePlayerData>\tCopyFileToCloud\tfalse\tSystem.Void\tSystem.Int32\tSystem.Boolean",
            "Assembly-CSharp\tDR.Save.SaveLoadManagerBase`1<SavePhotoData>\tCopyFileToCloud\tfalse\tSystem.Void\tSystem.Int32\tSystem.Boolean",
            "Assembly-CSharp\tDR.Save.SaveLoadManagerBase`1<DR.Save.SaveUserOptions>\tCopyFileToCloud\tfalse\tSystem.Void\tSystem.Int32\tSystem.Boolean",
            "Assembly-CSharp\tDR.Save.SaveLoadManagerBase`1<SaveData>\tWriteOnCloud\tfalse\tSystem.Void\tSystem.String\tSystem.Int32\tDR.Save.SaveSlotType",
            "Assembly-CSharp\tDR.Save.SaveLoadManagerBase`1<DR.Save.SavePlayerData>\tWriteOnCloud\tfalse\tSystem.Void\tSystem.String\tSystem.Int32\tDR.Save.SaveSlotType",
            "Assembly-CSharp\tDR.Save.SaveLoadManagerBase`1<SavePhotoData>\tWriteOnCloud\tfalse\tSystem.Void\tSystem.String\tSystem.Int32\tDR.Save.SaveSlotType",
            "Assembly-CSharp\tDR.Save.SaveLoadManagerBase`1<DR.Save.SaveUserOptions>\tWriteOnCloud\tfalse\tSystem.Void\tSystem.String\tSystem.Int32\tDR.Save.SaveSlotType",
            "Assembly-CSharp\tDR.Save.SaveLoadManagerBase`1<SaveData>\tWriteAllAutoSaveOnCloud\tfalse\tSystem.Void\tSystem.String",
            "Assembly-CSharp\tDR.Save.SaveLoadManagerBase`1<DR.Save.SavePlayerData>\tWriteAllAutoSaveOnCloud\tfalse\tSystem.Void\tSystem.String",
            "Assembly-CSharp\tDR.Save.SaveLoadManagerBase`1<SavePhotoData>\tWriteAllAutoSaveOnCloud\tfalse\tSystem.Void\tSystem.String",
            "Assembly-CSharp\tDR.Save.SaveLoadManagerBase`1<DR.Save.SaveUserOptions>\tWriteAllAutoSaveOnCloud\tfalse\tSystem.Void\tSystem.String",
            "Assembly-CSharp\tDR.Save.SaveLoadManagerBase`1<SaveData>\tSaveBackupData\tfalse\tSystem.Void",
            "Assembly-CSharp\tDR.Save.SaveLoadManagerBase`1<DR.Save.SavePlayerData>\tSaveBackupData\tfalse\tSystem.Void",
            "Assembly-CSharp\tDR.Save.SaveLoadManagerBase`1<SavePhotoData>\tSaveBackupData\tfalse\tSystem.Void",
            "Assembly-CSharp\tDR.Save.SaveLoadManagerBase`1<DR.Save.SaveUserOptions>\tSaveBackupData\tfalse\tSystem.Void",
            "Assembly-CSharp\tDR.Save.SaveLoadManagerBase`1<SaveData>\tSaveSlotWithJson\tfalse\tSystem.Boolean\tSystem.String\tSystem.Int32\tDR.Save.SaveSlotType",
            "Assembly-CSharp\tDR.Save.SaveLoadManagerBase`1<DR.Save.SavePlayerData>\tSaveSlotWithJson\tfalse\tSystem.Boolean\tSystem.String\tSystem.Int32\tDR.Save.SaveSlotType",
            "Assembly-CSharp\tDR.Save.SaveLoadManagerBase`1<SavePhotoData>\tSaveSlotWithJson\tfalse\tSystem.Boolean\tSystem.String\tSystem.Int32\tDR.Save.SaveSlotType",
            "Assembly-CSharp\tDR.Save.SaveLoadManagerBase`1<DR.Save.SaveUserOptions>\tSaveSlotWithJson\tfalse\tSystem.Boolean\tSystem.String\tSystem.Int32\tDR.Save.SaveSlotType",
            "Assembly-CSharp\tDR.Save.SaveLoadManagerBase`1<SaveData>\tSaveOnSelectedSlot\tfalse\tSystem.Boolean\tSystem.Int32\tDR.Save.SaveSlotType",
            "Assembly-CSharp\tDR.Save.SaveLoadManagerBase`1<DR.Save.SavePlayerData>\tSaveOnSelectedSlot\tfalse\tSystem.Boolean\tSystem.Int32\tDR.Save.SaveSlotType",
            "Assembly-CSharp\tDR.Save.SaveLoadManagerBase`1<SavePhotoData>\tSaveOnSelectedSlot\tfalse\tSystem.Boolean\tSystem.Int32\tDR.Save.SaveSlotType",
            "Assembly-CSharp\tDR.Save.SaveLoadManagerBase`1<DR.Save.SaveUserOptions>\tSaveOnSelectedSlot\tfalse\tSystem.Boolean\tSystem.Int32\tDR.Save.SaveSlotType",
            "Assembly-CSharp\tDR.Save.SaveLoadManagerBase`1<SaveData>\tTryLoadFromSlot\tfalse\tSystem.Boolean\tSystem.Int32\tDR.Save.SaveSlotType\tSaveData&",
            "Assembly-CSharp\tDR.Save.SaveLoadManagerBase`1<DR.Save.SavePlayerData>\tTryLoadFromSlot\tfalse\tSystem.Boolean\tSystem.Int32\tDR.Save.SaveSlotType\tDR.Save.SavePlayerData&",
            "Assembly-CSharp\tDR.Save.SaveLoadManagerBase`1<SavePhotoData>\tTryLoadFromSlot\tfalse\tSystem.Boolean\tSystem.Int32\tDR.Save.SaveSlotType\tSavePhotoData&",
            "Assembly-CSharp\tDR.Save.SaveLoadManagerBase`1<DR.Save.SaveUserOptions>\tTryLoadFromSlot\tfalse\tSystem.Boolean\tSystem.Int32\tDR.Save.SaveSlotType\tDR.Save.SaveUserOptions&",
            "Assembly-CSharp\tDR.Save.SaveLoadManagerBase`1<SaveData>\tTryLoadFromJson\tfalse\tSystem.Boolean\tSystem.String\tSaveData&",
            "Assembly-CSharp\tDR.Save.SaveLoadManagerBase`1<DR.Save.SavePlayerData>\tTryLoadFromJson\tfalse\tSystem.Boolean\tSystem.String\tDR.Save.SavePlayerData&",
            "Assembly-CSharp\tDR.Save.SaveLoadManagerBase`1<SavePhotoData>\tTryLoadFromJson\tfalse\tSystem.Boolean\tSystem.String\tSavePhotoData&",
            "Assembly-CSharp\tDR.Save.SaveLoadManagerBase`1<DR.Save.SaveUserOptions>\tTryLoadFromJson\tfalse\tSystem.Boolean\tSystem.String\tDR.Save.SaveUserOptions&",
            "Assembly-CSharp\tDR.Save.SaveLoadManagerBase`1<SaveData>\tSetLoadedData\tfalse\tSystem.Void\tSaveData",
            "Assembly-CSharp\tDR.Save.SaveLoadManagerBase`1<DR.Save.SavePlayerData>\tSetLoadedData\tfalse\tSystem.Void\tDR.Save.SavePlayerData",
            "Assembly-CSharp\tDR.Save.SaveLoadManagerBase`1<SavePhotoData>\tSetLoadedData\tfalse\tSystem.Void\tSavePhotoData",
            "Assembly-CSharp\tDR.Save.SaveLoadManagerBase`1<DR.Save.SaveUserOptions>\tSetLoadedData\tfalse\tSystem.Void\tDR.Save.SaveUserOptions",
            "Assembly-CSharp\tDR.Save.SaveLoadManagerBase`1<SaveData>\tLoadData\tfalse\tSystem.Void",
            "Assembly-CSharp\tDR.Save.SaveLoadManagerBase`1<DR.Save.SavePlayerData>\tLoadData\tfalse\tSystem.Void",
            "Assembly-CSharp\tDR.Save.SaveLoadManagerBase`1<SavePhotoData>\tLoadData\tfalse\tSystem.Void",
            "Assembly-CSharp\tDR.Save.SaveLoadManagerBase`1<DR.Save.SaveUserOptions>\tLoadData\tfalse\tSystem.Void",
            "Assembly-CSharp\tDR.Save.SaveLoadManagerBase`1<SaveData>\tLoadFromCloud\tfalse\tSystem.Void\tSystem.Int32\tDR.Save.SaveSlotType",
            "Assembly-CSharp\tDR.Save.SaveLoadManagerBase`1<DR.Save.SavePlayerData>\tLoadFromCloud\tfalse\tSystem.Void\tSystem.Int32\tDR.Save.SaveSlotType",
            "Assembly-CSharp\tDR.Save.SaveLoadManagerBase`1<SavePhotoData>\tLoadFromCloud\tfalse\tSystem.Void\tSystem.Int32\tDR.Save.SaveSlotType",
            "Assembly-CSharp\tDR.Save.SaveLoadManagerBase`1<DR.Save.SaveUserOptions>\tLoadFromCloud\tfalse\tSystem.Void\tSystem.Int32\tDR.Save.SaveSlotType",
            "Assembly-CSharp\tDR.Save.SaveLoadManagerBase`1<SaveData>\tLoadAllFromCloud\tfalse\tSystem.Void",
            "Assembly-CSharp\tDR.Save.SaveLoadManagerBase`1<DR.Save.SavePlayerData>\tLoadAllFromCloud\tfalse\tSystem.Void",
            "Assembly-CSharp\tDR.Save.SaveLoadManagerBase`1<SavePhotoData>\tLoadAllFromCloud\tfalse\tSystem.Void",
            "Assembly-CSharp\tDR.Save.SaveLoadManagerBase`1<DR.Save.SaveUserOptions>\tLoadAllFromCloud\tfalse\tSystem.Void",
            "Assembly-CSharp\tDR.Save.SaveLoadManagerBase`1<SaveData>\tWriteOldFileOnConvert\tfalse\tSystem.Void\tSystem.String\tSystem.String",
            "Assembly-CSharp\tDR.Save.SaveLoadManagerBase`1<DR.Save.SavePlayerData>\tWriteOldFileOnConvert\tfalse\tSystem.Void\tSystem.String\tSystem.String",
            "Assembly-CSharp\tDR.Save.SaveLoadManagerBase`1<SavePhotoData>\tWriteOldFileOnConvert\tfalse\tSystem.Void\tSystem.String\tSystem.String",
            "Assembly-CSharp\tDR.Save.SaveLoadManagerBase`1<DR.Save.SaveUserOptions>\tWriteOldFileOnConvert\tfalse\tSystem.Void\tSystem.String\tSystem.String",
            "Assembly-CSharp\tDR.Save.SaveLoadManagerBase`1<SaveData>\tCreateNew\tfalse\tSystem.Void\tSystem.Boolean",
            "Assembly-CSharp\tDR.Save.SaveLoadManagerBase`1<DR.Save.SavePlayerData>\tCreateNew\tfalse\tSystem.Void\tSystem.Boolean",
            "Assembly-CSharp\tDR.Save.SaveLoadManagerBase`1<SavePhotoData>\tCreateNew\tfalse\tSystem.Void\tSystem.Boolean",
            "Assembly-CSharp\tDR.Save.SaveLoadManagerBase`1<DR.Save.SaveUserOptions>\tCreateNew\tfalse\tSystem.Void\tSystem.Boolean",
            "Assembly-CSharp\tDR.Save.SaveLoadManagerBase`1<SaveData>\tCreateNewAndSave\tfalse\tSystem.Void\tSystem.Boolean",
            "Assembly-CSharp\tDR.Save.SaveLoadManagerBase`1<DR.Save.SavePlayerData>\tCreateNewAndSave\tfalse\tSystem.Void\tSystem.Boolean",
            "Assembly-CSharp\tDR.Save.SaveLoadManagerBase`1<SavePhotoData>\tCreateNewAndSave\tfalse\tSystem.Void\tSystem.Boolean",
            "Assembly-CSharp\tDR.Save.SaveLoadManagerBase`1<DR.Save.SaveUserOptions>\tCreateNewAndSave\tfalse\tSystem.Void\tSystem.Boolean",
            "Assembly-CSharp\tDR.Save.SaveLoadManagerBase`1<SaveData>\tDeleteSaveFile\tfalse\tSystem.Void\tSystem.String",
            "Assembly-CSharp\tDR.Save.SaveLoadManagerBase`1<DR.Save.SavePlayerData>\tDeleteSaveFile\tfalse\tSystem.Void\tSystem.String",
            "Assembly-CSharp\tDR.Save.SaveLoadManagerBase`1<SavePhotoData>\tDeleteSaveFile\tfalse\tSystem.Void\tSystem.String",
            "Assembly-CSharp\tDR.Save.SaveLoadManagerBase`1<DR.Save.SaveUserOptions>\tDeleteSaveFile\tfalse\tSystem.Void\tSystem.String",
            "Assembly-CSharp\tDR.Save.SaveLoadManagerBase`1<SaveData>\tDeleteSaveFile\tfalse\tSystem.Void",
            "Assembly-CSharp\tDR.Save.SaveLoadManagerBase`1<DR.Save.SavePlayerData>\tDeleteSaveFile\tfalse\tSystem.Void",
            "Assembly-CSharp\tDR.Save.SaveLoadManagerBase`1<SavePhotoData>\tDeleteSaveFile\tfalse\tSystem.Void",
            "Assembly-CSharp\tDR.Save.SaveLoadManagerBase`1<DR.Save.SaveUserOptions>\tDeleteSaveFile\tfalse\tSystem.Void",
            "Assembly-CSharp\tDR.Save.SaveLoadManagerBase`1<SaveData>\tCreateManagedData\tfalse\tSystem.Void",
            "Assembly-CSharp\tDR.Save.SaveLoadManagerBase`1<DR.Save.SavePlayerData>\tCreateManagedData\tfalse\tSystem.Void",
            "Assembly-CSharp\tDR.Save.SaveLoadManagerBase`1<SavePhotoData>\tCreateManagedData\tfalse\tSystem.Void",
            "Assembly-CSharp\tDR.Save.SaveLoadManagerBase`1<DR.Save.SaveUserOptions>\tCreateManagedData\tfalse\tSystem.Void",
            "Assembly-CSharp\tDR.Save.SaveLoadManagerBase`1<SaveData>\tOnLoadData\tfalse\tSystem.Void",
            "Assembly-CSharp\tDR.Save.SaveLoadManagerBase`1<DR.Save.SavePlayerData>\tOnLoadData\tfalse\tSystem.Void",
            "Assembly-CSharp\tDR.Save.SaveLoadManagerBase`1<SavePhotoData>\tOnLoadData\tfalse\tSystem.Void",
            "Assembly-CSharp\tDR.Save.SaveLoadManagerBase`1<DR.Save.SaveUserOptions>\tOnLoadData\tfalse\tSystem.Void",
        };
        public static List<GuestOutputTarget> Resolve()
        {
            var assemblies = new Dictionary<string, Assembly>(StringComparer.Ordinal);
            var result = new List<GuestOutputTarget>();
            var seen = new HashSet<MethodInfo>();
            foreach (string declaration in Declarations)
            {
                string[] parts = declaration.Split('\t');
                if (!assemblies.TryGetValue(parts[0], out Assembly assembly))
                { assembly = Assembly.Load(parts[0]); assemblies.Add(parts[0], assembly); }
                Type owner = ClosedBase(parts[1]) ?? assembly.GetType(parts[1].Replace('/', '+'), false);
                if (owner == null) throw new InvalidOperationException("Guest output owner is unavailable: " + parts[1]);
                bool isStatic = parts[3] == "true";
                string[] parameters = parts.Skip(5).ToArray();
                MethodInfo[] matches = owner.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly)
                    .Where(method => method.Name == parts[2] && method.IsStatic == isStatic && !method.IsGenericMethod && !method.IsAbstract &&
                        TypeKey(method.ReturnType) == parts[4] && method.GetParameters().Select(parameter => TypeKey(parameter.ParameterType)).SequenceEqual(parameters)).ToArray();
                if (matches.Length != 1 || matches[0].DeclaringType != owner || !seen.Add(matches[0]))
                    throw new InvalidOperationException("Guest output declaration mismatch: " + parts[1] + "::" + parts[2]);
                MethodInfo selected = matches[0];
                int outIndex = selected.Name == "TryLoadFromJson" ? 1 : selected.Name == "TryLoadFromSlot" ? 2 : -1;
                if (outIndex >= 0 && (selected.ReturnType != typeof(bool) || !selected.GetParameters()[outIndex].ParameterType.IsByRef))
                    throw new InvalidOperationException("Guest load output mismatch.");
                result.Add(new GuestOutputTarget(selected, FailureResult(selected), outIndex));
            }
            if (result.Count != ExpectedTargetCount) throw new InvalidOperationException("Guest output target inventory is incomplete.");
            return result;
        }
        private static Type ClosedBase(string name)
        {
            switch (name)
            {
                case "DR.Save.SaveLoadManagerBase`1<SaveData>": return typeof(SaveLoadManagerBase<SaveData>);
                case "DR.Save.SaveLoadManagerBase`1<DR.Save.SavePlayerData>": return typeof(SaveLoadManagerBase<SavePlayerData>);
                case "DR.Save.SaveLoadManagerBase`1<SavePhotoData>": return typeof(SaveLoadManagerBase<SavePhotoData>);
                case "DR.Save.SaveLoadManagerBase`1<DR.Save.SaveUserOptions>": return typeof(SaveLoadManagerBase<SaveUserOptions>);
                default: return null;
            }
        }
        private static string TypeKey(Type type)
        {
            if (type.IsByRef) return TypeKey(type.GetElementType()) + "&";
            if (type.IsArray) return TypeKey(type.GetElementType()) + "[]";
            if (type.IsGenericType) return type.GetGenericTypeDefinition().FullName.Replace('+', '/') + "<" + string.Join(",", type.GetGenericArguments().Select(TypeKey)) + ">";
            return type.FullName.Replace('+', '/');
        }
        private static object FailureResult(MethodInfo method)
        {
            Type type = method.ReturnType;
            if (type == typeof(void)) return null;
            if (type == typeof(bool)) return false;
            if (type == typeof(ulong)) return method.Name == "ISteamRemoteStorage_FileWriteStreamOpen" ? ulong.MaxValue : 0UL;
            if (type.IsEnum && (type.FullName == "Toolbox.SaveSystem.SaveResult" || type.FullName == "Toolbox.SaveSystem.DeleteResult"))
            {
                object failed = Enum.Parse(type, "Failed", false);
                int expected = type.FullName.EndsWith(".SaveResult", StringComparison.Ordinal) ? 2 : 1;
                if (Convert.ToInt32(failed) != expected) throw new InvalidOperationException("Guest output enum failure value changed.");
                return failed;
            }
            if (type.IsValueType && !type.IsEnum && (type.FullName == "Steamworks.SteamAPICall_t" || type.FullName == "Steamworks.UGCFileWriteStreamHandle_t"))
            {
                string fieldName = type.FullName.EndsWith(".SteamAPICall_t", StringComparison.Ordinal) ? "m_SteamAPICall" : "m_UGCFileWriteStreamHandle";
                FieldInfo field = type.GetField(fieldName, BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly);
                if (field == null || field.FieldType != typeof(ulong)) throw new InvalidOperationException("Guest output handle storage changed.");
                // Zero-initialize a CLR value wrapper; its ulong constructor
                // invokes native code and must not run in a blocking prefix.
                object failure = RuntimeHelpers.GetUninitializedObject(type);
                field.SetValue(failure, fieldName == "m_SteamAPICall" ? 0UL : ulong.MaxValue);
                return failure;
            }
            throw new InvalidOperationException("Unsupported guest output return: " + type.FullName);
        }
    }
}
