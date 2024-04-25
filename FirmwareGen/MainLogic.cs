using FirmwareGen.CommandLine;
using System;
using System.IO;

namespace FirmwareGen
{
    public static class MainLogic
    {
        // Method to verify the presence of all required components
        public static bool VerifyAllComponentsArePresent()
        {
            const string wimlib = "wimlib-imagex.exe";
            const string Img2Ffu = "Img2Ffu.exe";
            const string DriverUpdater = "DriverUpdater.exe";

            if (!File.Exists(wimlib) || !File.Exists(Img2Ffu) || !File.Exists(DriverUpdater))
            {
                Logging.Log("Some components could not be found.", Logging.LoggingLevel.Error);
                return false;
            }

            return true;
        }

        // Method to generate Windows FFU using specified options
        public static void GenerateWindowsFFU(GenerateWindowsFFUOptions options)
        {
            const string wimlib = "wimlib-imagex.exe";
            const string Img2Ffu = "Img2Ffu.exe";
            const string DriverUpdater = "DriverUpdater.exe";
            const string SystemPartition = "Y:";

            // Deserialize the device profile from the provided XML
            DeviceProfile deviceProfile = XmlUtils.Deserialize<DeviceProfile>(options.DeviceProfile);

            // Get a temporary VHD file for working with the image
            string TmpVHD = CommonLogic.GetBlankVHD(deviceProfile);
            string DiskId = VolumeUtils.MountVirtualHardDisk(TmpVHD, false);
            string VHDLetter = VolumeUtils.GetVirtualHardDiskLetterFromDiskID(DiskId);

            // Apply Windows image to the VHD from the specified DVD
            VolumeUtils.ApplyWindowsImageFromDVD(wimlib, options.WindowsDVD, options.WindowsIndex, VHDLetter);
            VolumeUtils.PerformSlabOptimization(VHDLetter);
            VolumeUtils.ApplyCompactFlagsToImage(VHDLetter);
            VolumeUtils.MountSystemPartition(DiskId, SystemPartition);
            VolumeUtils.ConfigureBootManager(VHDLetter, SystemPartition);
            VolumeUtils.UnmountSystemPartition(DiskId, SystemPartition);

            // Configure supplemental boot commands if provided in the device profile
            if (deviceProfile.SupplementaryBCDCommands.Length > 0)
            {
                VolumeUtils.MountSystemPartition(DiskId, SystemPartition);

                Logging.Log("Configuring supplemental boot");
                foreach (string command in deviceProfile.SupplementaryBCDCommands)
                {
                    VolumeUtils.RunProgram("bcdedit.exe", $@"{$@"/store {SystemPartition}\EFI\Microsoft\Boot\BCD "}{command}");
                }

                VolumeUtils.UnmountSystemPartition(DiskId, SystemPartition);
            }

            // Add drivers to the VHD
            Logging.Log("Adding drivers");
            VolumeUtils.RunProgram(DriverUpdater, $@"-d ""{options.DriverPack}{deviceProfile.DriverDefinitionPath}"" -r ""{options.DriverPack}"" -p ""{VHDLetter}""");

            // Dismount the temporary VHD
            VolumeUtils.DismountVirtualHardDisk(TmpVHD);

            // Generate FFU file from the VHD
            Logging.Log("Making FFU");
            VolumeUtils.RunProgram(Img2Ffu, $@"-i {TmpVHD} -f ""{options.Output}\{deviceProfile.FFUFileName}"" -c {deviceProfile.DiskSectorSize * 4} -s {deviceProfile.DiskSectorSize} -p ""{string.Join(";", deviceProfile.PlatformIDs)}"" -o {options.WindowsVer} -b 4000");

            // Delete the temporary VHD file
            Logging.Log("Deleting Temp VHD");
            File.Delete(TmpVHD);
        }
    }
}
