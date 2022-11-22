using CFLookup.Models;
using CurseForge.APIClient;
using CurseForge.APIClient.Models.Files;
using CurseForge.APIClient.Models.Mods;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Newtonsoft.Json;
using System.IO.Compression;
using System.Text;

namespace CFLookup.Pages
{
    public class ValidateManifestModel : PageModel
    {
        private readonly ApiClient _cfApiClient;

        public Mod? FoundMod { get; set; }
        public CurseForge.APIClient.Models.Files.File? FoundFile { get; set; }

        public CurseForgeMinecraftManifest? Manifest { get; set; }
        public string ErrorMessage { get; set; } = string.Empty;

        public List<Mod> UnavailableMods { get; set; } = new List<Mod>();

        public List<Mod> ModpackMods { get; set; } = new List<Mod>();

        readonly IHttpClientFactory _httpClientFactory;

        public ValidateManifestModel(ApiClient cfApiClient, IHttpClientFactory httpClientFactory)
        {
            _httpClientFactory = httpClientFactory;
            _cfApiClient = cfApiClient;
        }

        public async Task<IActionResult> OnGet(int projectId, int fileId)
        {
            var pro = await _cfApiClient.GetModAsync(projectId);

            if (pro != null)
            {
                FoundMod = pro.Data;
            }

            var errorMessage = new StringBuilder();

            if (pro.Data.ClassId == 4471 && (pro.Data.AllowModDistribution ?? true))
            {
                var dlFile = (await _cfApiClient.GetModFileAsync(projectId, fileId)).Data;

                FoundFile = dlFile;

                if (!string.IsNullOrWhiteSpace(dlFile.DownloadUrl))
                {
                    var hc = _httpClientFactory.CreateClient();
                    var fileBytes = await hc.GetByteArrayAsync(dlFile.DownloadUrl);

                    using var ms = new MemoryStream(fileBytes);
                    using var zf = new ZipArchive(ms, ZipArchiveMode.Read, false);
                    var manifest = zf.GetEntry("manifest.json");
                    if (manifest != null)
                    {
                        using var sr = new StreamReader(manifest.Open());
                        var fileContent = await sr.ReadToEndAsync();

                        var obj = JsonConvert.DeserializeObject<CurseForgeMinecraftManifest>(fileContent);

                        Manifest = obj;

                        var allowedDistribution = new Dictionary<int, bool>();

                        var unavailableProjects = GetUnavailableProjectsAsync(_cfApiClient, obj.Files.Select(f => new FileDependency
                        {
                            FileId = f.FileId,
                            ModId = f.ProjectId,
                            RelationType = FileRelationType.RequiredDependency
                        }).ToList());

                        var unavailableMods = new List<Mod>();

                        await foreach (var project in unavailableProjects)
                        {
                            unavailableMods.Add(project);
                        }

                        UnavailableMods = unavailableMods;
                    }
                }
                else
                {
                    errorMessage.AppendLine("No file download URL was available. This pack is probably broken.");
                }
            }
            else
            {
                errorMessage.AppendLine("This pack either does not allow downloads by third party clients, or is not a modpack");
            }

            ErrorMessage = errorMessage.ToString();

            return Page();
        }

        public HashSet<int> CheckedProjects = new();

        public async IAsyncEnumerable<Mod> GetUnavailableProjectsAsync(ApiClient cfApiClient, List<FileDependency> files)
        {
            var filteredList = new List<FileDependency>();

            foreach (var dep in files)
            {
                if (!CheckedProjects.Contains(dep.ModId))
                {
                    filteredList.Add(dep);
                    CheckedProjects.Add(dep.ModId);
                }
            }

            if (filteredList.Count == 0)
            {
                yield break;
            }

            var projectMods = await cfApiClient.GetModsByIdListAsync(new GetModsByIdsListRequestBody
            {
                ModIds = filteredList.Select(f => f.ModId).ToList()
            });

            foreach (var mod in projectMods.Data)
            {
                if (!ModpackMods.Any(m => m.Id == mod.Id))
                {
                    ModpackMods.Add(mod);
                }
            }

            var unavailableMods = projectMods.Data.Where(m => (m.AllowModDistribution ?? true) == false || !m.IsAvailable).ToList();

            foreach (var mod in unavailableMods)
            {
                yield return mod;
            }

            var filesWithAvailableProjects = filteredList.Where(f => !unavailableMods.Any(m => m.Id == f.ModId) && f.FileId != 0).Select(i => i.FileId).ToList();

            if (filesWithAvailableProjects.Count == 0)
            {
                yield break;
            }

            var projectFiles = await cfApiClient.GetFilesAsync(new GetModFilesRequestBody
            {
                FileIds = filesWithAvailableProjects
            });

            var filesWithRequiredDependencies = projectFiles.Data.Where(f => f.Dependencies.Any(d => d.RelationType == FileRelationType.RequiredDependency)).ToList();
            var dependencies = filesWithRequiredDependencies.SelectMany(f => f.Dependencies).Where(d => d.RelationType == FileRelationType.RequiredDependency).ToList();

            if (dependencies.Count == 0)
            {
                yield break;
            }

            var subDependencyMods = GetUnavailableProjectsAsync(cfApiClient, dependencies);
            await foreach (var dep in subDependencyMods)
            {
                yield return dep;
            }
        }
    }
}
