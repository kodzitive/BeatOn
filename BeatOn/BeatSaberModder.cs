﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.IO;
using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.OS;
using Android.Runtime;
using Android.Views;
using Android.Widget;
using QuestomAssets;
using QuestomAssets.Utils;
using Android.Support.V4.Content;
using Pxb.Android.Axml;

namespace BeatOn
{
    public class BeatSaberModder
    {        
        public const string APK_ASSETS_PATH = "assets/bin/Data/";
        public const string LIBMODLOADER_TARGET_FILE = "lib/armeabi-v7a/libmodloader.so";
        public const string LIBMODLOADER64_TARGET_FILE = "lib/arm64-v8a/libmodloader.so";
        public const string MOD_TAG_FILE = "beaton.modded";
        public const string BS_PLAYER_DATA_FILE = "/sdcard/Android/data/com.beatgames.beatsaber/files/PlayerData.dat";

        public event EventHandler<string> StatusUpdated;
        private Context _context;
        private string _tempApk;
        Action<string> _triggerUninstall;
        Action<string> _triggerInstall;
        public BeatSaberModder(Context context, Action<string> triggerUninstall, Action<string> triggerInstall)
        {
            _context = context;
            _triggerInstall = triggerInstall;
            _triggerUninstall = triggerUninstall;
        }

        internal string TempApk
        {
            get
            {
                if (_tempApk == null)
                {
                    CheckCleanupTempApk();
                    TryFindTempApk();
                }
                return _tempApk;
            }
            private set
            {
                _tempApk = value;
            }
        }

        public bool DoesTempApkExist
        {
            get
            {
                return TempApk != null && File.Exists(TempApk);
            }
        }

        public bool IsTempApkModded
        {
            get
            {
                if (TempApk == null)
                {
                    Log.LogErr("IsTempApkModded was called, but the TempApk does not exist!");
                    throw new ModException("IsTempApkModded was called, but the TempApk does not exist!");
                }
                return CheckApkHasModTagFile(TempApk);
            }
        }

        public void CheckCleanupTempApk()
        {
            try
            {
                if (IsBeatSaberInstalled && IsInstalledBeatSaberModded)
                {
                    string filename = Path.Combine(_context.ExternalCacheDir.AbsolutePath, "beatsabermod.apk");
                    if (File.Exists(filename))
                    {
                        File.Delete(filename);
                    }
                }
            }
            catch (Exception ex)
            {
                Log.LogErr("Exception trying to clean up the temp APK", ex);
            }
        }

        public bool IsInstalledBeatSaberModded
        {
            get
            {
#if EMULATOR
                return true;
#endif
                string bsApk = FindBeatSaberApk();
                if (bsApk == null)
                {
                    Log.LogErr($"Tried to call {nameof(IsInstalledBeatSaberModded)} when beat saber isn't installed.");
                    throw new ModException("Beat saber is not installed, cannot check if it is modded.");
                }
                try
                {
                    return CheckApkHasModTagFile(bsApk);
                }
                catch (Exception ex)
                {
                    Log.LogErr($"Exception in {nameof(IsInstalledBeatSaberModded)} when trying to check if it is modded.", ex);
                    throw new ModException("Error checking if installed beat saber is modded.", ex);
                }
            }
        }

        public bool IsBeatSaberInstalled
        {
            get
            {
#if EMULATOR
                return true;
#endif
                return FindBeatSaberApk() != null;
            }
        }

        public bool CheckIsTempApkReadyForInstall()
        {
            try
            {
                if (TempApk == null)
                    return false;
                return CheckApkHasModTagFile(TempApk);
            }
            catch (Exception ex)
            {
                Log.LogErr("Error checking if temp apk is ready for install.", ex);
                throw new ModException("Error checking if temp apk is ready for install.", ex);
            }
        }

        public void CopyOriginalBeatSaberApk(bool triggerUninstall)
        {
            UpdateStatus("Locating installed Beat Saber app...");
            string bsApkPath = FindBeatSaberApk();
            if (bsApkPath == null)
            {
                UpdateStatus("Unable to find installed Beat Saber app!");
                throw new ModException("Beat Saber does not seem to be installed, could not find its APK.");
            }
            UpdateStatus("Copying original Beat Saber APK to temporary location...");
            TempApk = Path.Combine(_context.ExternalCacheDir.AbsolutePath, "beatsabermod.apk");
            try
            {
                File.Copy(bsApkPath, TempApk, true);
                UpdateStatus("APK copied successfully!");

                BackupPlayerData();
                BackupOriginalApk();

                if (triggerUninstall)
                {
                    UpdateStatus("Prompting user to uninstall Beat Saber...");
                    TriggerPackageUninstall(bsApkPath);
                }
            }
            catch (Exception ex)
            {
                UpdateStatus("There was an error copying the original Beat Saber APK!");
                try
                {
                    File.Delete(TempApk);
                }
                catch
                { }
                throw new ModException("Problem copying original APK to temporary location.", ex);
            }
        }

        public void BackupPlayerData(bool onlyIfNewer = false, bool onlyIfBigger = false)
        {
            try
            {
                if (File.Exists(BS_PLAYER_DATA_FILE))
                {
                    if (File.Exists(Constants.BACKUP_FULL_PATH.CombineFwdSlash("PlayerData.dat")))
                    {
                        if (onlyIfNewer && File.GetLastWriteTimeUtc(BS_PLAYER_DATA_FILE) < File.GetLastWriteTimeUtc(Constants.BACKUP_FULL_PATH.CombineFwdSlash("PlayerData.dat")))
                        {
                            Log.LogMsg("Backup PlayerData.dat is newer than the real one, not backing it up.");
                            return;
                        }
                        if (onlyIfBigger && new FileInfo(Constants.BACKUP_FULL_PATH.CombineFwdSlash("PlayerData.dat")).Length > new FileInfo(BS_PLAYER_DATA_FILE).Length)
                        {
                            Log.LogMsg("Backup PlayerData.dat is bigger than the real one, not backing it up");
                            return;
                        }
                    }
                    try
                    {
                        if (File.Exists(Constants.BACKUP_FULL_PATH.CombineFwdSlash("PlayerData.dat")))
                        {
                            File.Copy(Constants.BACKUP_FULL_PATH.CombineFwdSlash("PlayerData.dat"), Constants.BACKUP_FULL_PATH.CombineFwdSlash("PlayerData2.dat"), true);
                        }
                    }
                    catch (Exception ex)
                    {
                        Log.LogErr("Exception trying to make second backup of playerdata.dat");
                    }
                    Log.LogMsg("Backing up PlayerData.dat");
                    MakeFullPath(Constants.BACKUP_FULL_PATH);
                    File.Copy(BS_PLAYER_DATA_FILE, Constants.BACKUP_FULL_PATH.CombineFwdSlash("PlayerData.dat"), true);
                }
            }
            catch (Exception ex)
            {
                Log.LogErr("Exception backing up player data", ex);
            }
        }

        public void RestorePlayerData(bool onlyIfBigger = false)
        {
            if (File.Exists(Constants.BACKUP_FULL_PATH.CombineFwdSlash("PlayerData.dat")))
            {
                if (onlyIfBigger && File.Exists(BS_PLAYER_DATA_FILE) && new FileInfo(Constants.BACKUP_FULL_PATH.CombineFwdSlash("PlayerData.dat")).Length < new FileInfo(BS_PLAYER_DATA_FILE).Length)
                {
                    Log.LogMsg("Backup PlayerData.dat is smaller than the real one, not restoring it.");
                    return;
                }
                Log.LogMsg("Restoring PlayerData.dat");
                MakeFullPath(Path.GetDirectoryName(BS_PLAYER_DATA_FILE));
                File.Copy(Constants.BACKUP_FULL_PATH.CombineFwdSlash("PlayerData.dat"), BS_PLAYER_DATA_FILE, true);
            }
        }

        public bool GetHasGoodBackup()
        {
            return File.Exists(Constants.BEATSABER_APK_BACKUP_FILE);
        }

        public bool GetHasCrapBackup()
        {
            //CheckCreateModdedBackup();
            return File.Exists(Constants.BEATSABER_APK_MODDED_BACKUP_FILE);
        }

        /// <summary>
        /// Checks if the backup exists, or creates a very unideal backup of the modded APK for the sake of uninstalling mods
        /// </summary>
        public void CheckCreateModdedBackup()
        {
            try
            {
                if (File.Exists(Constants.BEATSABER_APK_BACKUP_FILE))
                    return;
                Log.LogErr("WARNING: backup of original Beat Saber APK does not exist.  Will attempt to fall back to a post-mod backup.");
                if (File.Exists(Constants.BEATSABER_APK_MODDED_BACKUP_FILE))
                    return;
                Log.LogErr("Post-modded backup of Beat Saber APK does not exist, will attempt to create one.");

                if (!IsBeatSaberInstalled)
                {
                    Log.LogErr("Beat saber isn't even installed.  Can't make any sort of backup.");
                    return;
                }
                var apkPath = FindBeatSaberApk();
                if (apkPath == null)
                {
                    Log.LogErr("Unable to find the installed Beat Saber APK path.  Cannot make a post-mod backup.");
                    return;
                }
                Log.LogErr("Copying APK for unideal backup...");
                try
                {
                    MakeFullPath(Constants.BEATSABER_APK_MODDED_BACKUP_FILE.GetDirectoryFwdSlash());
                    File.Copy(apkPath, Constants.BEATSABER_APK_MODDED_BACKUP_FILE, true);
                }
                catch (Exception ex)
                {
                    Log.LogErr("Exception trying to copy APK for half assed backup.", ex);
                    return;
                }
                try
                {
                    Log.LogErr("Restoring names of modded APK asset files so they can serve as a backup...");
                    using (var apk = new ZipFileProvider(Constants.BEATSABER_APK_MODDED_BACKUP_FILE, FileCacheMode.None, false, QuestomAssets.Utils.FileUtils.GetTempDirectory()))
                    {
                        foreach (var assetFilename in apk.FindFiles(APK_ASSETS_PATH + "*"))
                        {
                            if (assetFilename.EndsWith(".bobak"))
                            {
                                apk.Rename(assetFilename, assetFilename.Substring(0, assetFilename.Length - 6));
                            }
                        }
                        apk.Save();
                    }
                }
                catch (Exception ex)
                {
                    Log.LogErr("Exception trying to make a half assed, post-mod backup while renaming files within the APK", ex);
                }
            }
            catch (Exception ex)
            {
                Log.LogErr($"Exception trying to check/create backups", ex);
            }
        }

        private void MakeFullPath(string path)
        {
            string[] splitPath = path.Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries);
            string curPath = "";
            for (int i = 0; i < splitPath.Length; i++)
            {
                curPath = Path.Combine(curPath, splitPath[i]);
                if (!Directory.Exists(curPath))
                    Directory.CreateDirectory(curPath);
            }
        }        

        public void BackupOriginalApk()
        {
            UpdateStatus("Locating installed Beat Saber app...");
            string bsApkPath = FindBeatSaberApk();
            if (bsApkPath == null)
            {
                UpdateStatus("Unable to find installed Beat Saber app!");
                throw new ModException("Beat Saber does not seem to be installed, could not find its APK.");
            }
            UpdateStatus("Verifying the installed APK isn't modded...");
            if (IsInstalledBeatSaberModded)
            {
                UpdateStatus("Installed beatsaber IS modded!");
                if (File.Exists(Constants.BEATSABER_APK_BACKUP_FILE))
                {
                    UpdateStatus("The APK backup already exists, not overwriting it with a modded one.");
                }
                else
                {
                    UpdateStatus("WARNING: There is not an APK backup and the installed beatsaber is already modded!");
                }
                return;
            }
            UpdateStatus("Copying original Beat Saber APK to a backup location...");
            MakeFullPath(Constants.BACKUP_FULL_PATH);
            try
            {
                File.Copy(bsApkPath, Constants.BEATSABER_APK_BACKUP_FILE, true);
                UpdateStatus("Backup created.");
            }
            catch (Exception ex)
            {
                Log.LogErr($"Failed to copy beatsaber APK from {bsApkPath} to {Constants.BEATSABER_APK_BACKUP_FILE}", ex);
                UpdateStatus("Failed to make a backup of the original APK!");
                throw;
            }
        }

        public void ApplyModToTempApk()
        {
            if (TempApk == null)
            {
                Log.LogErr("TempApk was null calling ModAndInstallBeatSaberApk, don't know where the temp apk is.");
                UpdateStatus("Unable to find the temporary APK!");
                throw new ModException("Unable to find the temporary APK.");
            }

            if (FindBeatSaberApk() != null)
            {
                UpdateStatus("The Beat Saber app is still installed, it must be removed before modding and reinstalling.");
                Log.LogErr("ModAndInstallBeatSaberApk: Beat Saber is still installed.  It needs to be gone to continue.");
                //throw new ModException("Beat Saber is still installed.  It needs to be uninstalled.");
            }

            bool tempApkModified = false;
            bool modFailed = false;
            //keep track of any temp files that may have been used so we can clean them up
            List<string> tempFiles = new List<string>();
            try
            {
                //// delete the assets relocation if it already exists in case the mod has been installed before
                if (Directory.Exists(Constants.ASSETS_RELOC_PATH))
                    Directory.Delete(Constants.ASSETS_RELOC_PATH, true);

                //// copy asset files from APK to /sdcard/wherever
                ExtractAssetsFromApkToExternalStorage(TempApk, new List<string>() {
                    "Managed",
                    "boot.config" });

                bool is64bit = IsApk64Bit(TempApk);

                //// copy libassetredirect.so to the mods folder
                InstallAssetRedirectMod(is64bit);

                //from this point on, the APK has been modified and isn't definitively recoverable if something goes wrong
                tempApkModified = true;

                //// modify classes.dex and inject the loadlibrary call for libmodloader.so
                InjectModLoaderToApk(TempApk, tempFiles);

                //// add libmodloader.so to the apk
                AddModLoaderToApk(TempApk);

                //// fix the manifest
                AddManifestModToApk(TempApk);

                //// add a 1 byte file to the APK so we know it's been modded to make verifying it later easier
                AddTagFileToApk(TempApk);

                //// re-sign the APK
                UpdateStatus("Re-signing the modded APK (this takes a minute)...");
                SignApk(TempApk);

                UpdateStatus("Restoring PlayerData.dat, hopefully this is a good time to do it.");
                RestorePlayerData();                
            }
            catch (Exception ex)
            {
                Log.LogErr("Exception modding temp APK.", ex);
                UpdateStatus("Something has gone wrong modding the APK!  You will need to reinstall beat saber and try again!");
                modFailed = true;
                throw ex;
            }
            finally
            {
                tempFiles.ForEach(x =>
                {
                    try
                    {
                        File.Delete(x);
                    }
                    catch (Exception ex)
                    {
                        Log.LogErr($"Could not remove temp file '{x}'!", ex);
                    }
                });
                if (modFailed && tempApkModified)
                {
                    Log.LogErr("The TempApk file is being deleted because something went wrong modding.");
                    try
                    {
                        var tempApk = TempApk;
                        TempApk = null;
                        File.Delete(tempApk);                        
                    }
                    catch (Exception ex)
                    {
                        Log.LogErr("Could not delete the TempApk file!", ex);
                    }
                }
            }
            UpdateStatus("Modding has completed!");
        }

        public void TriggerPackageInstall(string package = null)
        {
            if (TempApk == null)
                throw new Exception("TempApk is null, can't install it!");
            if (package == null)
                package = TempApk;
            if (_triggerInstall != null)
            {
                _triggerInstall(package);
                return;
            }
            PackageManager pkgMgr = _context.PackageManager;
            Intent intent = new Intent(Intent.ActionView);
            Android.Net.Uri apkURI = FileProvider.GetUriForFile(
                         _context,
                         _context.PackageName + ".provider", new Java.IO.File(package));
            intent.SetDataAndType(apkURI, "application/vnd.android.package-archive");
            intent.AddFlags(ActivityFlags.GrantReadUriPermission);
            //intent.SetDataAndType(Android.Net.Uri.FromFile(new Java.IO.File(packageApkPath)), "application/vnd.android.package-archive");
            _context.StartActivity(intent);
            _tempApk = null;
        }

        public void DeleteModsFromFolder()
        {
            if (Directory.Exists(Constants.MODS_FOLDER_NAME))
            {
                foreach (var dir in Directory.GetDirectories(Constants.MODS_FOLDER_NAME))
                {
                    try
                    {
                        Directory.Delete(dir, true);
                    }
                    catch (Exception ex)
                    {
                        Log.LogErr($"Failed to delete {dir} while deleting all mod folders");
                    }
                }
            }
        }

        public void DeleteModStatus()
        {
            if (File.Exists(Constants.ROOT_BEAT_ON_DATA_PATH.CombineFwdSlash(Constants.MOD_STATUS_FILE)))
                File.Delete(Constants.ROOT_BEAT_ON_DATA_PATH.CombineFwdSlash(Constants.MOD_STATUS_FILE));
        }

        public void ClearHookMods()
        {
            if (Directory.Exists(Constants.MODLOADER_MODS_PATH))
            {
                foreach (var file in Directory.GetFiles(Constants.MODLOADER_MODS_PATH))
                {
                    if (!file.EndsWith("libbeatonmod.so"))
                        try
                        {
                            File.Delete(file);
                        }
                        catch (Exception ex)
                        {
                            Log.LogErr($"Failed deleting mod {file}", ex);
                        }
                }
            }
        }

        public void ResetAssets()
        {
            UpdateStatus("Locating installed Beat Saber app...");
            string bsApkPath = FindBeatSaberApk();
            if (bsApkPath == null)
            {
                UpdateStatus("Unable to find installed Beat Saber app!");
                throw new ModException("Beat Saber does not seem to be installed, could not find its APK.");
            }
            UpdateStatus("Deleting existing external assets...");
            if (Directory.Exists(Constants.ASSETS_RELOC_PATH))
                Directory.Delete(Constants.ASSETS_RELOC_PATH, true);
            else
                UpdateStatus("External assets didn't seem to exist already");

            ExtractAssetsFromApkToExternalStorage(bsApkPath, new List<string>() {
                    "Managed",
                    "boot.config" }, false);
        }

        public void UninstallBeatSaber()
        {
            UpdateStatus("Locating installed Beat Saber app...");
            string bsApkPath = FindBeatSaberApk();
            if (bsApkPath == null)
            {
                UpdateStatus("Unable to find installed Beat Saber app!");
                throw new ModException("Beat Saber does not seem to be installed, could not find its APK.");
            }

            UpdateStatus("Triggering uninstall...");
            TriggerPackageUninstall(bsApkPath);
        }

        private void SignApk(string apkFilename)
        {
            try
            {
                using (var apk = new ZipFileProvider(apkFilename, FileCacheMode.None, false, QuestomAssets.Utils.FileUtils.GetTempDirectory()))
                {
                    ApkSigner signer = new ApkSigner(QuestomAssets.BeatSaber.BSConst.DebugCertificatePEM);
                    signer.Sign(apk);
                }
                UpdateStatus("APK signed!");
            }
            catch (Exception ex)
            {
                Log.LogErr($"Exception signing the APK {apkFilename}!", ex);
                UpdateStatus("Error re-signing the APK!");
                throw new ModException($"Exception signing the APK {apkFilename}!", ex);
            }
        }

        public void CleanupTempApk()
        {
            if (TempApk == null)
                return;

            try
            {
                File.Delete(TempApk);
                TempApk = null;
            }
            catch (Exception ex)
            {
                Log.LogErr("Unable to delete temp APK.", ex);
                throw new ModException("Unable to delete temp APK.", ex);
            }

        }

        private bool CheckApkHasModTagFile(string apkFilename)
        {
            using (var apk = new ZipFileProvider(apkFilename, FileCacheMode.None, true, QuestomAssets.Utils.FileUtils.GetTempDirectory()))
            {
                if (apk.FileExists(MOD_TAG_FILE))
                    return true;
            }
            return false;
        }

        private void UpdateStatus(string message)
        {
            StatusUpdated?.Invoke(this, message);
        }

        public void TriggerPackageUninstall(string packageApkPath)
        {
            if (_triggerUninstall != null)
            {
                _triggerUninstall(packageApkPath);
                return;
            }
            PackageManager pkgMgr = _context.PackageManager;

            Intent intent = new Intent(Intent.ActionDelete, Android.Net.Uri.FromParts("package",
                    pkgMgr.GetPackageArchiveInfo(packageApkPath, 0).PackageName, null));
            _context.StartActivity(intent);
        }

        private string FindBeatSaberApk()
        {
            Intent mainIntent = new Intent(Intent.ActionMain, null);
            mainIntent.AddCategory(Intent.CategoryInfo);
            var pkgAppsList = _context.PackageManager.QueryIntentActivities(mainIntent, 0);
            foreach (var info in pkgAppsList)
            {

                if (info.ActivityInfo.PackageName == "com.beatgames.beatsaber")
                {
                    //found beat saber
                    return info.ActivityInfo.ApplicationInfo.PublicSourceDir;
                }
            }
            return null;
        }

        private string GetFromApkToFile(string apkFileName, string getFilename, string destinationFile = null)
        {
            string tempFile;
            if (destinationFile != null)
            {
                tempFile = destinationFile;
            }
            else
            {
                tempFile = Java.IO.File.CreateTempFile(getFilename, "", _context.ExternalCacheDir).AbsolutePath;
            }
            using (var apk = new ZipFileProvider(apkFileName, FileCacheMode.None, false, QuestomAssets.Utils.FileUtils.GetTempDirectory()))
            {
                using (var fs = File.Open(tempFile, FileMode.Create, FileAccess.ReadWrite))
                {
                    using (var readStream = apk.GetReadStream(getFilename, true))
                        readStream.CopyTo(fs);
                }
            }
            return tempFile;
        }

        private void SaveFileToApk(string apkFileName, string toFileName, string sourceFile)
        {
            using (var apk = new ZipFileProvider(apkFileName, FileCacheMode.None, false, QuestomAssets.Utils.FileUtils.GetTempDirectory()))
            {
                using (var fs = File.OpenRead(sourceFile))
                {
                    apk.QueueWriteStream(toFileName, fs, true, true);
                    apk.Save();
                }
            }
        }

        private void TryFindTempApk()
        {
            try
            {
                var tempApk = Path.Combine(_context.ExternalCacheDir.AbsolutePath, "beatsabermod.apk");
                if (File.Exists(tempApk))
                {
                    //TODO: more validation to make sure it isn't busted?
                    TempApk = tempApk;
                }
            }
            catch (Exception ex)
            {
                Log.LogErr("Exception trying to find the temp apk.", ex);
                _tempApk = null;
            }
        }

        private void InjectModLoaderToApk(string apkFilename, List<string> tempFiles)
        {
            try
            {
                UpdateStatus("Getting classes.dex from APK...");

                string classesDexTempFile = GetFromApkToFile(apkFilename, "classes.dex");
                tempFiles.Add(classesDexTempFile);
                string moddedClassesDexTempFile = Java.IO.File.CreateTempFile("moddedclasses.dex", "", _context.ExternalCacheDir).AbsolutePath;
                tempFiles.Add(moddedClassesDexTempFile);
                using (Com.Emulamer.Installerhelper.DexHelper dexHelper = new Com.Emulamer.Installerhelper.DexHelper())
                {
                    if (!dexHelper.InjectDex(new Java.IO.File(classesDexTempFile), new Java.IO.File(moddedClassesDexTempFile)))
                    {
                        UpdateStatus("classes.dex appears to already be modified");
                        Log.LogMsg("Tried to inject static constructor to classes.dex, but it seems to already have one.");
                    }
                    else
                    {
                        UpdateStatus("Writing modded classes.dex to the APK...");
                        SaveFileToApk(apkFilename, "classes.dex", moddedClassesDexTempFile);
                    }
                }
            }
            catch (Exception ex)
            {
                Log.LogErr($"Exception in {nameof(InjectModLoaderToApk)}!", ex);
                UpdateStatus("Error injecting modloader!");
                throw new ModException("Unable to inject mod loader!", ex);
            }
        }

        public string GetBeatSaberVersion()
        {
            if (!IsBeatSaberInstalled)
                return null;

            try
            {
                Android.Content.PM.PackageInfo pInfo = _context.PackageManager.GetPackageInfo("com.beatgames.beatsaber", 0);
                return pInfo.VersionName;
            }
            catch (PackageManager.NameNotFoundException e)
            {
                Log.LogErr("The Beat Saber package could not be found!");
                throw;
            }
            catch (Exception ex)
            {
                Log.LogErr("Exception trying to determine Beat Saber version", ex);
                throw;
            }
        }

        public string GetAssetsVersion()
        {
            if (!Directory.Exists(Constants.ASSETS_RELOC_PATH))
                return null;           
            try
            {
                string verFileName = Constants.ASSETS_RELOC_PATH.CombineFwdSlash("assets.version");
                if (!File.Exists(verFileName))
                {
                    //if the assets.version file doesn't exist but globalgamemanagers does, probably it's an upgrade from 0.1.0.0 or earlier that didn't write this file
                    //  create one assuming that it is BS version 1.1.0
                    if (File.Exists(Constants.ASSETS_RELOC_PATH.CombineFwdSlash("globalgamemanagers")))
                    {
                        File.WriteAllText(verFileName, "1.1.0");
                    }
                    else
                    {
                        return null;
                    }
                }

                string ver = File.ReadAllText(verFileName);
                if (string.IsNullOrWhiteSpace(ver))
                    return null;

                return ver.Trim();
            }
            catch (Exception ex)
            {
                Log.LogErr("Exception trying to get extracted assets files version", ex);
                return null;
            }
        }

        private void ExtractAssetsFromApkToExternalStorage(string apkFilename, List<string> excludePaths = null, bool renameAssets = true)
        {
            UpdateStatus("Extracting assets files from the APK to external storage...");
            using (var apk = new ZipFileProvider(apkFilename, FileCacheMode.None, !renameAssets, QuestomAssets.Utils.FileUtils.GetTempDirectory()))
            {
                foreach (var assetFilename in apk.FindFiles(APK_ASSETS_PATH + "*"))
                {
                    string relativeFilename = assetFilename.Substring(APK_ASSETS_PATH.Length);
                    if (excludePaths != null)
                    {
                        if (excludePaths.Any(x => relativeFilename.StartsWith(x)))
                        {
                            Log.LogMsg($"The asset file {assetFilename} ({relativeFilename}) is not included in assets that should be extracted, skipping.");
                            continue;
                        }
                    }
                    Log.LogMsg($"Extracting {assetFilename}...");
                    string targetFile = Path.Combine(Constants.ASSETS_RELOC_PATH, relativeFilename);
                    string dirName = Path.GetDirectoryName(targetFile);
                    try
                    {
                        if (!Directory.Exists(dirName))
                        {
                            Log.LogMsg($"Assets target directory doesn't exist, creating {dirName}");
                            Directory.CreateDirectory(dirName);
                        }
                    }
                    catch (Exception ex)
                    {
                        Log.LogErr($"Unable to create directory {dirName}!", ex);
                        UpdateStatus("Failed to create assets directory in external storage!");
                        throw new ModException($"Unable to create directory {dirName}!", ex);
                    }
                    try
                    {
                        using (var readStream = apk.GetReadStream(assetFilename, true))
                        {
                            if (targetFile.EndsWith(".bobak"))
                                targetFile = targetFile.Substring(0, targetFile.Length - 6);

                            using (var fs = File.Open(targetFile, FileMode.Create, FileAccess.Write))
                            {
                                readStream.CopyTo(fs);
                            }
                        }
                        if (!assetFilename.EndsWith(".bobak") && renameAssets)
                            apk.Rename(assetFilename, assetFilename + ".bobak");
                    }
                    catch (Exception ex)
                    {
                        Log.LogErr($"Failed to extract {assetFilename} to {targetFile}", ex);
                        UpdateStatus("Failed extracting an asset from the APK to external storage!");
                        throw new ModException($"Failed to extract {assetFilename} to {targetFile}", ex);
                    }
                }
                if (renameAssets)
                {
                    apk.Save();
                    var apkVer = GetAPKVersion(apkFilename);
                    var verPath = Constants.ASSETS_RELOC_PATH.CombineFwdSlash("assets.version");
                    if (File.Exists(verPath))
                    {
                        File.Delete(verPath);
                    }
                    if (string.IsNullOrWhiteSpace(apkVer))
                    {
                        Log.LogErr($"Got an empty version from apk file '{apkFilename}'");
                        throw new Exception($"Got empty version from apk file '{apkFilename}'");
                    }
                    File.WriteAllText(verPath, apkVer);
                }
            }
        }

        private string GetAPKVersion(string fullApkPath)
        {
            try
            {
                Android.Content.PM.PackageInfo info = _context.PackageManager.GetPackageArchiveInfo(fullApkPath, 0);
                return info.VersionName;
            }
            catch (Exception ex)
            {
                Log.LogErr($"Exception trying to get version from APK '{fullApkPath}'", ex);
                throw;
            }
        }

        private List<string> GetMissingPermissions(string apkFilename, List<string> requiredPermissions)
        {
            try
            {
                var tt = _context.PackageManager.GetPackageArchiveInfo(apkFilename, PackageInfoFlags.Permissions);
                if (tt.RequestedPermissions == null)
                    return requiredPermissions.ToList();

                return requiredPermissions.Where(x => !tt.RequestedPermissions.Any(y => y == x)).ToList();
            }
            catch (Exception ex)
            {
                Log.LogErr($"Exception trying to check APK '{apkFilename}' for write external storage permission", ex);
                throw;
            }
        }

        private void AddAndroidPermissions(string apkFilename, List<string> permissions)
        {
            if (permissions == null || permissions.Count < 1)
                throw new ArgumentException("At least one permission must be specified to add.");
            try
            {
                using (var apk = new ZipFileProvider(apkFilename, FileCacheMode.None, false, QuestomAssets.Utils.FileUtils.GetTempDirectory()))
                {
                    byte[] manifest = apk.Read("AndroidManifest.xml");
                    AxmlWriter writer = new WritePermissionAxmlWriter(permissions);
                    AxmlReader reader = new AxmlReader(manifest);

                    reader.Accept(writer);
                    var outData = writer.ToByteArray();
                    apk.Write("AndroidManifest.xml", outData, true);
                    apk.Save();
                }
            }
            catch (Exception ex)
            {
                Log.LogErr("Exception trying to add permissions to AndroidManifest.xml", ex);
                throw;
            }
        }

        private class WritePermissionAxmlWriter : Pxb.Android.Axml.AxmlWriter
        {
            private List<string> _permissionsToAdd;
            public WritePermissionAxmlWriter(List<string> permissionsToAdd)
            {
                _permissionsToAdd = permissionsToAdd;
            }

            public override NodeVisitor Child(string ns, string name)
            {
                if (name == "manifest")
                {
                    var baseChild = base.Child(ns, name);
                    foreach (var permission in _permissionsToAdd)
                    {
                        var c = baseChild.Child(ns, "uses-permission");
                        c.Attr("http://schemas.android.com/apk/res/android", "name", 16842755, 3, new Java.Lang.String(permission));
                    }
                    return baseChild;
                }
                return base.Child(ns, name);
            }
        }

        private void AddManifestModToApk(string apkFilename)
        {
            UpdateStatus("Modding the manifest in the APK...");
            try
            {
                var permissions = GetMissingPermissions(apkFilename, new List<string>() { "android.permission.WRITE_EXTERNAL_STORAGE",
                                                                        "android.permission.READ_EXTERNAL_STORAGE",
                                                                        "android.permission.INTERNET" });
                if (permissions.Count < 1)
                {
                    Log.LogMsg($"APK '{apkFilename}' is not missing any required permissions in its manifest.");
                    return;
                }

                Log.LogMsg($"APK '{apkFilename}' is missing permissions: {string.Join(", ", permissions)}");
                AddAndroidPermissions(apkFilename, permissions);
            }
            catch (Exception ex)
            {
                Log.LogErr("Error modding the manifest in the APK", ex);
                UpdateStatus("Error modding the manifest in the APK!");
                throw new ModException("Error modding the manifest in the APK", ex);
            }
        }

        private bool IsApk64Bit(string apkFilename)
        {
            UpdateStatus("Checking if APK is 64-bit...");
            try
            {
                using (var apk = new ZipFileProvider(apkFilename, FileCacheMode.None, true, QuestomAssets.Utils.FileUtils.GetTempDirectory()))
                {
                    if (apk.DirectoryExists(LIBMODLOADER64_TARGET_FILE.GetDirectoryFwdSlash()))
                    {
                        UpdateStatus("APK is 64 bit");
                        return true;
                    }
                    if (apk.DirectoryExists(LIBMODLOADER_TARGET_FILE.GetDirectoryFwdSlash()))
                    {
                        UpdateStatus("APK is 32 bit");
                        return false;
                    }
                    UpdateStatus("Can't find a libs folder for either 32 or 64 bit...");
                    throw new ModException("Unable to find library folder to determine 32 or 64 bit.");
                }
            }
            catch (Exception ex)
            {
                Log.LogErr("Error determining if APK is 64 bit!", ex);
                UpdateStatus("Unable to determine if APK is 32 or 64 bit!");
                throw new ModException("Error determining if APK is 64 bit!", ex);
            }
        }

        private void AddModLoaderToApk(string apkFilename)
        {
            UpdateStatus("Adding the libmodloader.so file to the APK...");
            try
            {
                using (var apk = new ZipFileProvider(apkFilename, FileCacheMode.None, false, QuestomAssets.Utils.FileUtils.GetTempDirectory()))
                {
                    using (var resStream = _context.Resources.OpenRawResource(Resource.Raw.libmodloader))
                    {
                        using (var resStream64 = _context.Resources.OpenRawResource(Resource.Raw.libmodloader64))
                        {
                            if (apk.DirectoryExists(LIBMODLOADER_TARGET_FILE.GetDirectoryFwdSlash()))
                            {
                                apk.QueueWriteStream(LIBMODLOADER_TARGET_FILE, resStream, true, true);
                            }
                            if (apk.DirectoryExists(LIBMODLOADER64_TARGET_FILE.GetDirectoryFwdSlash()))
                            {
                                apk.QueueWriteStream(LIBMODLOADER64_TARGET_FILE, resStream64, true, true);
                            }
                            apk.Save();
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Log.LogErr("Error adding libmodloader.so to APK", ex);
                UpdateStatus("Error adding modloader to the APK!");
                throw new ModException("Error adding libmodloader.so to APK", ex);
            }
        }

        private void InstallAssetRedirectMod(bool is64Bit)
        {
            UpdateStatus("Installing asset redirection mod ...");
            try
            {
                string dirName = Constants.MODLOADER_MODS_PATH;
                try
                {
                    if (!Directory.Exists(dirName))
                    {
                        Log.LogMsg($"Mods target directory doesn't exist, creating {dirName}");
                        Directory.CreateDirectory(dirName);
                    }
                }
                catch (Exception ex)
                {
                    Log.LogErr($"Unable to create directory {dirName}!", ex);
                    UpdateStatus("Failed to create mods directory in external storage!");
                    throw new ModException($"Unable to create directory {dirName}!", ex);
                }
                if (is64Bit)
                {
                    using (var resStream = _context.Resources.OpenRawResource(Resource.Raw.libbeatonmod64))
                    {
                        using (var fs = File.Open(Path.Combine(Constants.MODLOADER_MODS_PATH, "libbeatonmod.so"), FileMode.Create, FileAccess.Write))
                        {
                            resStream.CopyTo(fs);
                        }
                    }
                } else
                {
                    using (var resStream = _context.Resources.OpenRawResource(Resource.Raw.libbeatonmod))
                    {
                        using (var fs = File.Open(Path.Combine(Constants.MODLOADER_MODS_PATH, "libbeatonmod.so"), FileMode.Create, FileAccess.Write))
                        {
                            resStream.CopyTo(fs);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Log.LogErr("Error copying libbeatonmod.so!", ex);
                UpdateStatus("Error installing asset redirection mod!");
                throw new ModException("Error copying libbeatonmod.so", ex);
            }
        }

        private void AddTagFileToApk(string apkFilename)
        {
            using (var apk = new ZipFileProvider(apkFilename, FileCacheMode.None, false, QuestomAssets.Utils.FileUtils.GetTempDirectory()))
            {
                if (apk.FileExists(MOD_TAG_FILE))
                {
                    Log.LogMsg("APK file already had the mod's tag file.");
                    return;
                }
                apk.Write(MOD_TAG_FILE, new byte[1], true, false);
                apk.Save();
            }
        }

    }
}