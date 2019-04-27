﻿using System;
using System.IO;
using BattleTech.Save.Core;
using Harmony;

namespace MechEngineer
{
    //[HarmonyPatch(typeof(WriteLocation), MethodType.Constructor, typeof(string), typeof(bool))]
    public static class WriteLocation_Constructor_Patch
    {
        public static void Prefix(ref string rootPath, ref bool usePlatform)
        {
            var fullPath = Path.GetFullPath(rootPath).TrimEnd(Path.DirectorySeparatorChar);
            var dirName = Path.GetFileName(fullPath);
            var platformPath = NewSaveFolderHandlers.PathByPlatform(usePlatform);
            rootPath = Path.Combine(platformPath, dirName);
            usePlatform = false;
        }
    }
}