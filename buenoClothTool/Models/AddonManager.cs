using CodeWalker.GameFiles;
using buenoClothTool.Constants;
using buenoClothTool.Controls;
using buenoClothTool.Extensions;
using buenoClothTool.Helpers;
using buenoClothTool.Models.Drawable;
using buenoClothTool.Models.Other;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Linq;

namespace buenoClothTool.Models
{
    public class AddonManagerDesign : AddonManager
    {
        public AddonManagerDesign()
        {
            ProjectName = "Design";
            Addons = [];
            Addons.Add(new Addon("design"));
            SelectedAddon = Addons.First();
        }
    }

    public class AddonManager : INotifyPropertyChanged
    {
        private abstract record WorkItem;
        private record DrawableWorkItem(
            string FilePath,
            Enums.SexType Sex,
            string BasePath,
            PedFile Ymt,
            PedAlternativeVariations PedAltVariations,
            Dictionary<(int, int), MCComponentInfo> CompInfoDict,
            Dictionary<(int, int), MCPedPropMetaData> PedPropMetaDataDict,
            Dictionary<(int, bool), int> TypeNumericCounts
        ) : WorkItem;
        private record CompletionMarker(TaskCompletionSource Tcs) : WorkItem;

        private readonly BlockingCollection<WorkItem> _drawableQueue = new();
        private readonly Task _drawableProcessingTask;

        public static readonly object AddonsLock = new();

        private static readonly Regex AlternateRegex = new(@"_\w_\d+\.ydd$", RegexOptions.Compiled | RegexOptions.IgnoreCase);
        private static readonly Regex PhysicsRegex = new(@"\.yld$", RegexOptions.Compiled);

        private const int BATCH_SIZE = 40;

        public event PropertyChangedEventHandler PropertyChanged;

        public string ProjectName { get; set; }

        public ObservableCollection<string> IgnoredDuplicateGroups { get; set; } = [];

        [JsonInclude]
        private string SavedAt => DateTime.Now.ToString("dd-MM-yyyy HH:mm:ss");

        public ObservableCollection<string> Groups { get; set; } = [];
        public ObservableCollection<string> Tags { get; set; } = [];

        [JsonIgnore]
        public ObservableCollection<MoveMenuItem> MoveMenuItems { get; set; } = [];

        private ObservableCollection<Addon> _addons = [];
        public ObservableCollection<Addon> Addons
        {
            get { return _addons; }
            set
            {
                if (_addons != value)
                {
                    _addons = value;
                    OnPropertyChanged();
                }
            }
        }

        private Addon _selectedAddon;
        [JsonIgnore]
        public Addon SelectedAddon
        {
            get { return _selectedAddon; }
            set
            {
                if (_selectedAddon != value)
                {
                    _selectedAddon = value;
                    OnPropertyChanged();
                }
            }
        }

        private bool _isPreviewEnabled;
        [JsonIgnore]
        public bool IsPreviewEnabled
        {
            get { return _isPreviewEnabled; }
            set
            {
                _isPreviewEnabled = value;
                OnPropertyChanged();
            }
        }

        public AddonManager()
        {
            _drawableProcessingTask = Task.Run(ProcessDrawableQueue);
        }

        public void CreateAddon()
        {
            var name = "Addon " + (Addons.Count + 1);
            Addons.Add(new Addon(name));
            OnPropertyChanged("Addons");
        }

        private async Task<PedAlternativeVariations> LoadPedAlternativeVariationsFileAsync(string dirPath, string addonName)
        {
            try
            {
                var pedAltVariationsFiles = await Task.Run(() =>
                    Directory.GetFiles(dirPath, "pedalternativevariations*.meta", SearchOption.AllDirectories)
                        .Where(x => x.Contains(addonName))
                        .ToArray());

                if (pedAltVariationsFiles.Length == 0) return null;

                var pedAltVariationsFile = pedAltVariationsFiles.FirstOrDefault();
                if (pedAltVariationsFile == null) return null;

                var xmlDoc = await Task.Run(() => XDocument.Load(pedAltVariationsFile));
                return PedAlternativeVariations.FromXml(xmlDoc);
            }
            catch (Exception ex)
            {
                LogHelper.Log($"Error loading pedalternativevariations.meta: {ex.Message}", Views.LogType.Warning);
                return null;
            }
        }

        public async Task LoadAddon(string path, bool shouldSetProjectName = false)
        {
            var dirPath = Path.GetDirectoryName(path);
            var addonName = Path.GetFileNameWithoutExtension(path);

            Enums.SexType sex = addonName.Contains("mp_m_freemode_01") ? Enums.SexType.male : Enums.SexType.female;

            string genderSpecificPart = sex == Enums.SexType.male ? "mp_m_freemode_01" : "mp_f_freemode_01";
            string addonNameWithoutGender = addonName.Replace(genderSpecificPart, "").TrimStart('_');

            if (shouldSetProjectName)
            {
                MainWindow.AddonManager.ProjectName = addonNameWithoutGender;
            }

            var (yddFiles, ymtFile, yldFiles) = await Task.Run(() =>
            {
                string pattern = $@"^{genderSpecificPart}(_p)?.*?{Regex.Escape(addonNameWithoutGender)}\^";
                var compiledPattern = new Regex(pattern, RegexOptions.IgnoreCase | RegexOptions.Compiled);

                var allFiles = Directory.GetFiles(dirPath, "*.*", SearchOption.AllDirectories);

                var ydds = allFiles
                    .Where(f => f.EndsWith(".ydd", StringComparison.OrdinalIgnoreCase))
                    .Where(f => compiledPattern.IsMatch(Path.GetFileName(f)))
                    .OrderBy(x =>
                    {
                        var number = FileHelper.GetDrawableNumberFromFileName(Path.GetFileName(x));
                        return number ?? int.MaxValue;
                    })
                    .ThenBy(Path.GetFileName, StringComparer.Ordinal)
                    .ToArray();

                var ymt = allFiles
                    .Where(f => f.EndsWith(".ymt", StringComparison.OrdinalIgnoreCase))
                    .FirstOrDefault(x => x.Contains(addonName));

                var ylds = allFiles
                    .Where(f => f.EndsWith(".yld", StringComparison.OrdinalIgnoreCase))
                    .Where(f => compiledPattern.IsMatch(Path.GetFileName(f)))
                    .OrderBy(x =>
                    {
                        var number = FileHelper.GetDrawableNumberFromFileName(Path.GetFileName(x));
                        return number ?? int.MaxValue;
                    })
                    .ThenBy(Path.GetFileName, StringComparer.Ordinal)
                    .ToArray();

                return (ydds, ymt, ylds);
            });

            if (yddFiles.Length == 0)
            {
                CustomMessageBox.Show($"No .ydd files found for selected .meta file ({Path.GetFileName(path)})", "Error");
                return;
            }

            if (ymtFile == null)
            {
                CustomMessageBox.Show($"No .ymt file found for selected .meta file ({Path.GetFileName(path)})", "Error");
                return;
            }

            var ymt = new PedFile();
            var ymtBytes = await FileHelper.ReadAllBytesAsync(ymtFile);
            RpfFile.LoadResourceFile(ymt, ymtBytes, 2);

            var pedAltVariations = await LoadPedAlternativeVariationsFileAsync(dirPath, addonNameWithoutGender);

            var mergedFiles = yddFiles.Concat(yldFiles).ToArray();

            await AddDrawables(mergedFiles, sex, ymt, dirPath, pedAltVariations);

            GC.Collect();
            GC.WaitForPendingFinalizers();
        }

        public Task AddDrawables(string[] filePaths, Enums.SexType sex, PedFile ymt = null, string basePath = null, PedAlternativeVariations pedAltVariations = null)
        {
            var tcs = new TaskCompletionSource();

            Dictionary<(int, bool), int> typeNumericCounts = [];
            Dictionary<(int, int), MCComponentInfo> compInfoDict = [];
            Dictionary<(int, int), MCPedPropMetaData> pedPropMetaDataDict = [];

            if (ymt is not null)
            {
                var hasCompInfos = ymt.VariationInfo.CompInfos != null;
                if (hasCompInfos)
                {
                    foreach (var compInfo in ymt.VariationInfo.CompInfos)
                    {
                        var key = (compInfo.ComponentType, compInfo.ComponentIndex);
                        compInfoDict[key] = compInfo;
                    }
                }

                var hasProps = ymt.VariationInfo.PropInfo.PropMetaData != null && ymt.VariationInfo.PropInfo.Data.numAvailProps > 0;
                if (hasProps)
                {
                    foreach (var pedPropMetaData in ymt.VariationInfo.PropInfo.PropMetaData)
                    {
                        var key = (pedPropMetaData.Data.anchorId, pedPropMetaData.Data.propId);
                        pedPropMetaDataDict[key] = pedPropMetaData;
                    }
                }
            }

            foreach (var filePath in filePaths)
            {
                var workItem = new DrawableWorkItem(
                    filePath,
                    sex,
                    basePath,
                    ymt,
                    pedAltVariations,
                    compInfoDict,
                    pedPropMetaDataDict,
                    typeNumericCounts
                );
                _drawableQueue.Add(workItem);
            }

            _drawableQueue.Add(new CompletionMarker(tcs));
            return tcs.Task;
        }

        private async void ProcessDrawableQueue()
        {
            var pendingDrawables = new List<GDrawable>();
            var pendingDrawableSourceNumbers = new Dictionary<GDrawable, int>();

            foreach (var workItem in _drawableQueue.GetConsumingEnumerable())
            {
                if (workItem is CompletionMarker marker)
                {
                    if (pendingDrawables.Count > 0)
                    {
                        await ProcessBatchDuplicatesAndAdd(pendingDrawables);
                        pendingDrawables.Clear();
                        pendingDrawableSourceNumbers.Clear();
                    }

                    marker.Tcs.SetResult();
                    continue;
                }

                var (filePath, sex, basePath, ymt, pedAltVariations, compInfoDict, pedPropMetaDataDict, typeNumericCounts) = (DrawableWorkItem)workItem;

                var (isProp, drawableType) = await FileHelper.ResolveDrawableType(filePath);
                if (drawableType == -1) continue;

                await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    if (Addons.Count == 0) CreateAddon();
                });

                var drawablesOfType = new List<GDrawable>();
                await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    foreach (var addon in Addons)
                    {
                        drawablesOfType.AddRange(addon.Drawables.Where(x => x.TypeNumeric == drawableType && x.IsProp == isProp && x.Sex == sex));
                    }
                });

                if (AlternateRegex.IsMatch(filePath))
                {
                    if (filePath.EndsWith("_1.ydd", StringComparison.OrdinalIgnoreCase))
                    {
                        var number = FileHelper.GetDrawableNumberFromFileName(Path.GetFileName(filePath));
                        if (number == null)
                        {
                            LogHelper.Log($"Could not find associated YDD file for first person file: {filePath}, please do it manually", Views.LogType.Warning);
                            continue;
                        }

                        var foundDrawable = drawablesOfType.FirstOrDefault(x => x.Number == number);
                        if (foundDrawable == null)
                        {
                            foundDrawable = pendingDrawables.FirstOrDefault(x =>
                                x.TypeNumeric == drawableType &&
                                x.IsProp == isProp &&
                                x.Sex == sex &&
                                pendingDrawableSourceNumbers.TryGetValue(x, out var srcNum) &&
                                srcNum == number);
                        }

                        if (foundDrawable != null)
                        {
                            try
                            {
                                var firstPersonFileNameWithoutExtension = $"{foundDrawable.Id}_firstperson";
                                var firstPersonRelativePath = await FileHelper.CopyToProjectAssetsWithReplaceAsync(filePath, firstPersonFileNameWithoutExtension);
                                await System.Windows.Application.Current.Dispatcher.InvokeAsync(() => foundDrawable.FirstPersonPath = firstPersonRelativePath);
                            }
                            catch (Exception ex)
                            {
                                LogHelper.Log($"Failed to copy first person file to project assets: {ex.Message}. Using original path.", Views.LogType.Warning);
                                await System.Windows.Application.Current.Dispatcher.InvokeAsync(() => foundDrawable.FirstPersonPath = filePath);
                            }
                        }
                        else
                        {
                            LogHelper.Log($"Could not find associated YDD file for first person file: {filePath}, please do it manually", Views.LogType.Warning);
                        }
                    }
                    continue;
                }

                if (PhysicsRegex.IsMatch(filePath))
                {
                    var number = FileHelper.GetDrawableNumberFromFileName(Path.GetFileName(filePath));
                    if (number == null)
                    {
                        LogHelper.Log($"Could not find associated YDD file for this YLD: {filePath}, please do it manually", Views.LogType.Warning);
                        continue;
                    }

                    var foundDrawable = drawablesOfType.FirstOrDefault(x => x.Number == number);
                    if (foundDrawable == null)
                    {
                        foundDrawable = pendingDrawables.FirstOrDefault(x =>
                            x.TypeNumeric == drawableType &&
                            x.IsProp == isProp &&
                            x.Sex == sex &&
                            pendingDrawableSourceNumbers.TryGetValue(x, out var srcNum) &&
                            srcNum == number);
                    }

                    if (foundDrawable != null)
                    {
                        try
                        {
                            var physicsFileNameWithoutExtension = $"{foundDrawable.Id}_cloth";
                            var physicsRelativePath = await FileHelper.CopyToProjectAssetsWithReplaceAsync(filePath, physicsFileNameWithoutExtension);
                            await System.Windows.Application.Current.Dispatcher.InvokeAsync(() => foundDrawable.ClothPhysicsPath = physicsRelativePath);
                        }
                        catch (Exception ex)
                        {
                            LogHelper.Log($"Failed to copy cloth physics file to project assets: {ex.Message}. Using original path.", Views.LogType.Warning);
                            await System.Windows.Application.Current.Dispatcher.InvokeAsync(() => foundDrawable.ClothPhysicsPath = filePath);
                        }
                    }
                    else
                    {
                        LogHelper.Log($"Could not find associated YDD file for this YLD: {filePath}, please do it manually", Views.LogType.Warning);
                    }
                    continue;
                }

                var drawable = await FileHelper.CreateDrawableAsync(filePath, sex, isProp, drawableType, 0);

                var sourceNumber = FileHelper.GetDrawableNumberFromFileName(Path.GetFileName(filePath));
                if (sourceNumber.HasValue)
                {
                    pendingDrawableSourceNumbers[drawable] = sourceNumber.Value;
                }

                if (!string.IsNullOrEmpty(basePath) && filePath.StartsWith(basePath))
                {
                    var extractedGroup = ExtractGroupFromPath(filePath, basePath, sex, isProp);
                    if (!string.IsNullOrWhiteSpace(extractedGroup))
                    {
                        drawable.Group = extractedGroup;
                        await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
                        {
                            if (!Groups.Contains(extractedGroup))
                            {
                                Groups.Add(extractedGroup);
                                GroupManager.Instance.AddGroup(extractedGroup);
                            }
                        });
                    }
                }

                if (ymt is not null)
                {
                    var key = (drawableType, isProp);
                    if (typeNumericCounts.TryGetValue(key, out int value))
                        typeNumericCounts[key] = ++value;
                    else
                        typeNumericCounts[key] = 1;

                    var ymtKey = (drawable.TypeNumeric, typeNumericCounts[(drawable.TypeNumeric, drawable.IsProp)] - 1);
                    if (compInfoDict.TryGetValue(ymtKey, out MCComponentInfo compInfo))
                    {
                        var list = EnumHelper.GetFlags((int)compInfo.Data.flags);
                        drawable.Audio = compInfo.Data.pedXml_audioID.ToString();
                        drawable.SelectedFlags = list.ToObservableCollection();
                        if (compInfo.Data.pedXml_expressionMods.f4 != 0)
                        {
                            drawable.EnableHighHeels = true;
                            drawable.HighHeelsValue = compInfo.Data.pedXml_expressionMods.f4;
                        }
                    }

                    if (drawable.IsProp && pedPropMetaDataDict.TryGetValue(ymtKey, out MCPedPropMetaData pedPropMetaData))
                    {
                        drawable.Audio = pedPropMetaData.Data.audioId.ToString();
                        drawable.RenderFlag = pedPropMetaData.Data.renderFlags.ToString();
                        var list = EnumHelper.GetFlags((int)pedPropMetaData.Data.propFlags);
                        drawable.SelectedFlags = list.ToObservableCollection();
                        if (pedPropMetaData.Data.expressionMods.f0 != 0)
                        {
                            drawable.EnableHairScale = true;
                            drawable.HairScaleValue = Math.Abs(pedPropMetaData.Data.expressionMods.f0);
                        }
                    }
                }

                if (pedAltVariations != null && !drawable.IsProp)
                {
                    string pedName = sex == Enums.SexType.male ? "mp_m_freemode_01" : "mp_f_freemode_01";
                    var pedVariation = pedAltVariations.Peds.FirstOrDefault(p => p.Name == pedName);
                    if (pedVariation != null)
                    {
                        foreach (var alternateSwitch in pedVariation.Switches)
                        {
                            var matchingAsset = alternateSwitch.SourceAssets.FirstOrDefault(asset =>
                                asset.Component == drawable.TypeNumeric && asset.Index == drawable.Number);
                            if (matchingAsset != null)
                            {
                                drawable.HidesHair = true;
                                break;
                            }
                        }
                    }
                }

                pendingDrawables.Add(drawable);

                if (pendingDrawables.Count >= BATCH_SIZE)
                {
                    await ProcessBatchDuplicatesAndAdd(pendingDrawables);
                    pendingDrawables.Clear();
                }
            }
        }

        private async Task ProcessBatchDuplicatesAndAdd(List<GDrawable> drawables)
        {
            // Simplesmente adicionamos tudo. 
            // O DuplicateDetector registrará as duplicatas internamente ao chamar AddDrawableInternal.
            // O usuário gerencia isso depois na tela de Duplicate Inspector.
            foreach (var drawable in drawables)
            {
                await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    AddDrawableInternal(drawable);
                });
            }

            await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
            {
                Addons.Sort(true);
            });
        }

        private static string ExtractGroupFromPath(string filePath, string basePath, Enums.SexType sex, bool isProp)
        {
            try
            {
                filePath = Path.GetFullPath(filePath);
                basePath = Path.GetFullPath(basePath);

                var relativePath = Path.GetRelativePath(basePath, Path.GetDirectoryName(filePath));

                var parts = relativePath.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

                if (parts.Length > 3)
                {
                    int genderIndex = -1;
                    string expectedGenderFolder = sex == Enums.SexType.male ? "[male]" : "[female]";

                    for (int i = 0; i < parts.Length; i++)
                    {
                        if (parts[i].Equals(expectedGenderFolder, StringComparison.OrdinalIgnoreCase))
                        {
                            genderIndex = i;
                            break;
                        }
                    }

                    if (genderIndex >= 0 && genderIndex < parts.Length - 2)
                    {
                        var groupParts = parts.Skip(genderIndex + 1).Take(parts.Length - genderIndex - 2).ToArray();
                        if (groupParts.Length > 0)
                        {
                            return string.Join("/", groupParts);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                LogHelper.Log($"Error extracting group from path: {ex.Message}", Views.LogType.Warning);
            }

            return null;
        }

        public void AddDrawable(GDrawable drawable)
        {
            lock (AddonsLock)
            {
                AddDrawableInternal(drawable);
            }
        }

        private void AddDrawableInternal(GDrawable drawable)
        {
            lock (AddonsLock)
            {
                int nextNumber = 0;
                int currentAddonIndex = 0;
                Addon currentAddon;

                while (currentAddonIndex < Addons.Count)
                {
                    currentAddon = Addons[currentAddonIndex];
                    int countOfType = currentAddon.Drawables.Count(x => x.TypeNumeric == drawable.TypeNumeric && x.IsProp == drawable.IsProp && x.Sex == drawable.Sex);

                    if (countOfType >= GlobalConstants.MAX_DRAWABLES_IN_ADDON)
                    {
                        currentAddonIndex++;
                        continue;
                    }

                    nextNumber = countOfType;
                    break;
                }

                if (currentAddonIndex < Addons.Count)
                {
                    currentAddon = Addons[currentAddonIndex];
                }
                else
                {
                    currentAddon = new Addon("Addon " + (currentAddonIndex + 1));
                    Addons.Add(currentAddon);
                }

                drawable.IsNew = true;
                drawable.Number = nextNumber;
                drawable.SetDrawableName();

                currentAddon.Drawables.Add(drawable);

                DuplicateDetector.RegisterDrawable(drawable); // Isso marca a duplicata no sistema para ver depois
                SaveHelper.SetUnsavedChanges(true);
            }
        }

        public void DeleteDrawables(List<GDrawable> drawables)
        {
            SaveHelper.SetUnsavedChanges(true);

            foreach (GDrawable drawable in drawables.ToList())
            {
                DuplicateDetector.UnregisterDrawable(drawable);

                var ownerAddon = Addons.FirstOrDefault(a => a.Drawables.Contains(drawable));

                if (ownerAddon != null)
                {
                    ownerAddon.Drawables.Remove(drawable);
                }
                else
                {
                    LogHelper.Log($"Warning: Tried to delete drawable '{drawable.Name}' but it was not found in any active Addon list.", Views.LogType.Warning);
                }

                if (SettingsHelper.Instance.AutoDeleteFiles)
                {
                    try
                    {
                        foreach (var texture in drawable.Textures)
                        {
                            var fullPath = texture.FullFilePath;
                            if (File.Exists(fullPath))
                            {
                                File.Delete(fullPath);
                            }
                        }

                        var drawableFullPath = drawable.FullFilePath;
                        if (File.Exists(drawableFullPath))
                        {
                            File.Delete(drawableFullPath);
                        }
                    }
                    catch (Exception ex)
                    {
                        LogHelper.Log($"Failed to delete files for drawable '{drawable.Name}': {ex.Message}", Views.LogType.Warning);
                    }
                }

                if (ownerAddon != null && ownerAddon.Drawables.Count == 0)
                {
                    DeleteAddon(ownerAddon);
                }
                else if (ownerAddon != null)
                {
                    ownerAddon.Drawables.Sort(true);
                }
            }
        }

        public void MoveDrawable(GDrawable drawable, Addon targetAddon)
        {
            if (drawable == null || targetAddon == null)
            {
                var nullParam = drawable == null ? nameof(drawable) : nameof(targetAddon);
                throw new ArgumentNullException(nullParam, $"{nullParam} cannot be null.");
            }

            var currentAddon = Addons.FirstOrDefault(a => a.Drawables.Contains(drawable));
            if (currentAddon != null)
            {
                currentAddon.Drawables.Remove(drawable);

                drawable.Number = currentAddon.GetNextDrawableNumber(drawable.TypeNumeric, drawable.IsProp, drawable.Sex);
                drawable.SetDrawableName();

                targetAddon.Drawables.Add(drawable);
            }
        }

        public int GetTotalDrawableAndTextureCount()
        {
            return Addons.Sum(addon => addon.GetTotalDrawableAndTextureCount());
        }

        private void DeleteAddon(Addon addon)
        {
            if (Addons.Count <= 1)
            {
                return;
            }

            int index = Addons.IndexOf(addon);
            if (index < 0) { return; }

            Addons.RemoveAt(index);
            AdjustAddonNames();
        }

        private void AdjustAddonNames()
        {
            for (int i = 0; i < Addons.Count; i++)
            {
                Addons[i].Name = $"Addon {i + 1}";
            }

            OnPropertyChanged("Addons");
        }

        protected void OnPropertyChanged([CallerMemberName] string name = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        }
    }
}