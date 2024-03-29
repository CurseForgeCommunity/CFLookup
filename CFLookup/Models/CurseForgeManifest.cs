﻿namespace CFLookup.Models
{
    public class CurseForgeManifest
    {
        public string ManifestType { get; set; }
        public int ManifestVersion { get; set; }
        public string Name { get; set; }
        public string Version { get; set; }
        public string Author { get; set; }
        public List<CurseForgeManifestFile> Files { get; set; } = new List<CurseForgeManifestFile>();
        public string Overrides { get; set; }
    }

    public class CurseForgeManifestFile
    {
        public int ProjectId { get; set; }
        public int FileId { get; set; }
        public bool Required { get; set; }
    }

    public class CurseForgeMinecraftManifest : CurseForgeManifest
    {
        public CurseForgeMinecraftInfo Minecraft { get; set; }

        public class CurseForgeMinecraftInfo
        {
            public string Version { get; set; }
            public List<CurseForgeMinecraftModLoader> Modloaders { get; set; }

            public class CurseForgeMinecraftModLoader
            {
                public string Id { get; set; }
                public bool Primary { get; set; }
            }
        }
    }
}
