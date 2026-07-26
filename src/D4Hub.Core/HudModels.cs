using System.ComponentModel;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace D4Hub.Core;

public enum EquipmentSlotKind
{
    Helm,
    Chest,
    Gloves,
    Pants,
    Boots,
    Ranged,
    MainHand,
    OffHand,
    Amulet,
    RingLeft,
    RingRight,
    BarbarianBludgeoning,
    BarbarianDualWieldMainHand,
    BarbarianSlashing,
    BarbarianDualWieldOffHand
}

public enum HudAffixColorKind
{
    Core,
    Offensive,
    Defensive,
    Utility,
    Skill,
    Other
}

public sealed class HudAffixDisplayLine
{
    public string Name { get; set; } = string.Empty;
    public List<string> Values { get; set; } = new();
    public HudAffixColorKind ColorKind { get; set; } = HudAffixColorKind.Other;
    public bool IsTempered { get; set; }
    public bool IsMasterworked { get; set; }
    public bool IsTransfigured { get; set; }

    public string CompactText => Name;
    public string ValueText => Values.Count == 0 ? Name : $"{Name} {string.Join(" / ", Values)}";
}

public sealed class EquipmentAffixRule : INotifyPropertyChanged
{
    private string _slotLabel = string.Empty;
    private string _itemName = string.Empty;
    private string _mandatoryText = string.Empty;
    private string _optionalText = string.Empty;
    private List<ItemAffixRecord> _affixes = new();
    private bool _isEnabled = true;
    private double _anchorX;
    private double _anchorY;
    private double _displayWidth = 180;

    public EquipmentSlotKind Slot { get; set; }

    public string SlotLabel
    {
        get => _slotLabel;
        set => SetField(ref _slotLabel, value ?? string.Empty);
    }

    public string ItemName
    {
        get => _itemName;
        set => SetField(ref _itemName, value?.Trim() ?? string.Empty);
    }

    public string MandatoryText
    {
        get => _mandatoryText;
        set
        {
            if (SetField(ref _mandatoryText, value?.Trim() ?? string.Empty))
            {
                NotifyDisplayTextChanged();
            }
        }
    }

    public string OptionalText
    {
        get => _optionalText;
        set
        {
            if (SetField(ref _optionalText, value?.Trim() ?? string.Empty))
            {
                NotifyDisplayTextChanged();
            }
        }
    }

    public List<ItemAffixRecord> Affixes
    {
        get => _affixes;
        set
        {
            if (SetField(ref _affixes, value ?? new List<ItemAffixRecord>()))
            {
                NotifyDisplayTextChanged();
            }
        }
    }

    [JsonIgnore]
    public string CompactText => HudAffixTextFormatter.CreateCompactText(Affixes, MandatoryText, OptionalText);

    [JsonIgnore]
    public string ValueText => HudAffixTextFormatter.CreateValueText(Affixes, MandatoryText, OptionalText);

    [JsonIgnore]
    public IReadOnlyList<HudAffixDisplayLine> DisplayLines =>
        HudAffixTextFormatter.CreateDisplayLines(Affixes, MandatoryText, OptionalText);

    public bool IsEnabled
    {
        get => _isEnabled;
        set => SetField(ref _isEnabled, value);
    }

    public double AnchorX
    {
        get => _anchorX;
        set => SetField(ref _anchorX, value);
    }

    public double AnchorY
    {
        get => _anchorY;
        set => SetField(ref _anchorY, value);
    }

    public double DisplayWidth
    {
        get => _displayWidth;
        set => SetField(ref _displayWidth, value);
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void NotifyDisplayTextChanged()
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(CompactText)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(ValueText)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(DisplayLines)));
    }

    private bool SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return false;
        }

        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        return true;
    }
}

public static partial class HudAffixTextFormatter
{
    private static readonly char[] FallbackSeparators = ['\r', '\n', '·', '/', '／'];

    private readonly record struct DisplayLineKey(
        string Name,
        bool IsTempered,
        bool IsMasterworked,
        bool IsTransfigured);

    public static string CreateCompactText(
        IEnumerable<ItemAffixRecord>? affixes,
        string mandatoryText = "",
        string optionalText = "") =>
        string.Join(
            Environment.NewLine,
            CreateDisplayLines(affixes, mandatoryText, optionalText).Select(line => line.CompactText));

    public static string CreateValueText(
        IEnumerable<ItemAffixRecord>? affixes,
        string mandatoryText = "",
        string optionalText = "") =>
        string.Join(
            Environment.NewLine,
            CreateDisplayLines(affixes, mandatoryText, optionalText).Select(line => line.ValueText));

    public static IReadOnlyList<HudAffixDisplayLine> CreateDisplayLines(
        IEnumerable<ItemAffixRecord>? affixes,
        string mandatoryText = "",
        string optionalText = "")
    {
        var lines = new List<HudAffixDisplayLine>();
        var indexes = new Dictionary<DisplayLineKey, int>();
        foreach (var affix in affixes ?? Enumerable.Empty<ItemAffixRecord>())
        {
            var name = GetShortName(affix);
            if (string.IsNullOrWhiteSpace(name))
            {
                continue;
            }

            var key = new DisplayLineKey(
                name,
                affix.IsTempered,
                affix.CriticalUpgradeLevel > 0,
                Contains(affix.SourceKey ?? string.Empty, "Transfiguration"));
            if (!indexes.TryGetValue(key, out var index))
            {
                index = lines.Count;
                indexes.Add(key, index);
                lines.Add(new HudAffixDisplayLine
                {
                    Name = name,
                    ColorKind = GetColorKind(name),
                    IsTempered = key.IsTempered,
                    IsMasterworked = key.IsMasterworked,
                    IsTransfigured = key.IsTransfigured
                });
            }

            var value = GetValueText(affix);
            if (!string.IsNullOrWhiteSpace(value) && !lines[index].Values.Contains(value, StringComparer.Ordinal))
            {
                lines[index].Values.Add(value);
            }
        }

        if (lines.Count == 0)
        {
            lines.AddRange(CreateFallbackNames(mandatoryText, optionalText).Select(name => new HudAffixDisplayLine
            {
                Name = name,
                ColorKind = GetColorKind(name)
            }));
        }

        return lines
            .OrderBy(line => line.IsTransfigured ? 1 : 0)
            .ToList();
    }

    public static HudAffixColorKind GetColorKind(string name)
    {
        if (name is "敏捷" or "力量" or "智力" or "意力" or "主属性") return HudAffixColorKind.Core;
        if (name is "毒伤" or "持续伤" or "易伤" or "武器伤" || name.Contains("伤害", StringComparison.Ordinal)) return HudAffixColorKind.Offensive;
        if (name is "生命" or "护甲" or "全抗" || name.Contains("减免", StringComparison.Ordinal)) return HudAffixColorKind.Defensive;
        if (name is "毒灌注" or "全技能" or "灌注强度" || name.Contains("技能", StringComparison.Ordinal)) return HudAffixColorKind.Skill;
        if (name is "冷却" or "移速" or "回能" or "幸运" || name.Contains("速度", StringComparison.Ordinal) || name.Contains("资源", StringComparison.Ordinal)) return HudAffixColorKind.Utility;
        return HudAffixColorKind.Other;
    }

    public static string GetShortName(ItemAffixRecord affix)
    {
        var key = affix.SourceKey ?? string.Empty;
        var name = affix.Name ?? string.Empty;
        var source = $"{key} {name}";

        if (Contains(source, "PoisonImbue") || Contains(source, "Category_Imbuements")) return "毒灌注";
        if (Contains(source, "CoreStat_Dexterity")) return "敏捷";
        if (Contains(source, "CooldownReduction")) return "冷却";
        if (Contains(source, "LifeMax") || Contains(source, "_Life") || name.Contains("生命上限", StringComparison.Ordinal)) return "生命";
        if (Contains(source, "Transfiguration_DamageTypePercent_Poison")) return "毒素伤害";
        if (Contains(source, "DamageType_Poison")) return "毒素伤害增倍";
        if (Contains(source, "Damage_DoT")) return "持续伤";
        if (Contains(source, "Resistance_All")) return "全抗";
        if (Contains(source, "Armor") || name.Contains("护甲", StringComparison.Ordinal)) return "护甲";
        if (Contains(source, "Damage_to_Vulnerable")) return "易伤";
        if (Contains(source, "MovementSpeed") || Contains(source, "Movement_Speed")) return "移速";
        if (Contains(source, "SkillRankBonus_AllSkills")) return "全技能";
        if (Contains(source, "Imbue_Potency")) return "灌注强度";
        if (Contains(source, "LuckyHit_Resource")) return "回能";
        if (Contains(source, "Weapon_Damage")) return "武器伤";
        if (Contains(source, "Luck") || name.Contains("幸运一击", StringComparison.Ordinal)) return "幸运";

        return ShortenFallbackName(name);
    }

    private static List<string> CreateFallbackNames(string mandatoryText, string optionalText) =>
        $"{mandatoryText}{Environment.NewLine}{optionalText}"
            .Split(FallbackSeparators, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(ShortenFallbackName)
            .Where(name => !string.IsNullOrWhiteSpace(name) && !name.StartsWith("威能", StringComparison.Ordinal))
            .Distinct(StringComparer.Ordinal)
            .ToList();

    private static string ShortenFallbackName(string name)
    {
        var trimmed = name.Trim().TrimStart('点', '至');
        if (trimmed.Contains("敏捷", StringComparison.Ordinal)) return "敏捷";
        if (trimmed.Contains("毒素灌注", StringComparison.Ordinal) || trimmed.Contains("毒灌注", StringComparison.Ordinal)) return "毒灌注";
        if (trimmed.Contains("冷却", StringComparison.Ordinal)) return "冷却";
        if (trimmed.Contains("生命", StringComparison.Ordinal)) return "生命";
        if (trimmed.Contains("持续", StringComparison.Ordinal)) return "持续伤";
        if (trimmed.Contains("毒素", StringComparison.Ordinal) && trimmed.Contains("伤害增倍", StringComparison.Ordinal)) return "毒素伤害增倍";
        if (trimmed.Contains("毒素", StringComparison.Ordinal)) return "毒素伤害";
        if (trimmed.Contains("毒伤", StringComparison.Ordinal)) return "毒伤";
        if (trimmed.Contains("全元素抗性", StringComparison.Ordinal) || trimmed.Contains("抗性", StringComparison.Ordinal)) return "全抗";
        if (trimmed.Contains("护甲", StringComparison.Ordinal)) return "护甲";
        if (trimmed.Contains("易伤", StringComparison.Ordinal)) return "易伤";
        if (trimmed.Contains("移动速度", StringComparison.Ordinal)) return "移速";
        if (trimmed.Contains("所有技能", StringComparison.Ordinal)) return "全技能";
        if (trimmed.Contains("灌注效果强度", StringComparison.Ordinal)) return "灌注强度";
        if (trimmed.Contains("幸运一击", StringComparison.Ordinal)) return "幸运";
        return trimmed;
    }

    private static string GetValueText(ItemAffixRecord affix)
    {
        if (string.IsNullOrWhiteSpace(affix.DisplayText)
            || string.Equals(affix.DisplayText, affix.SourceKey, StringComparison.Ordinal)
            || string.Equals(affix.DisplayText, affix.Name, StringComparison.Ordinal))
        {
            return string.Empty;
        }

        var matches = AffixValuePattern().Matches(affix.DisplayText);
        if (matches.Count == 0)
        {
            return string.Empty;
        }

        var value = matches[^1].Value.Replace(" ", string.Empty, StringComparison.Ordinal);
        if (value.EndsWith("%[x]", StringComparison.OrdinalIgnoreCase))
        {
            value = $"x{value[..^3]}";
        }

        return value.Replace('×', 'x');
    }

    private static bool Contains(string source, string value) =>
        source.Contains(value, StringComparison.OrdinalIgnoreCase);

    [GeneratedRegex(@"[+x×]?\s*\d[\d,]*(?:\.\d+)?%?(?:\[x\])?", RegexOptions.IgnoreCase)]
    private static partial Regex AffixValuePattern();
}

public sealed class BuildVisualFingerprint
{
    public string LeftHash { get; set; } = string.Empty;
    public string CenterHash { get; set; } = string.Empty;
    public string RightHash { get; set; } = string.Empty;
    public DateTimeOffset CapturedAt { get; set; } = DateTimeOffset.UtcNow;

    public bool IsComplete =>
        TryParseHash(LeftHash, out _)
        && TryParseHash(CenterHash, out _)
        && TryParseHash(RightHash, out _);

    internal static bool TryParseHash(string value, out ulong hash) =>
        ulong.TryParse(value, System.Globalization.NumberStyles.HexNumber, null, out hash);
}

public sealed class BuildProfile : INotifyPropertyChanged
{
    private string _name = string.Empty;
    private string _className = string.Empty;
    private string _variant = string.Empty;

    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    public string Name
    {
        get => _name;
        set => SetField(ref _name, value ?? string.Empty);
    }

    public string ClassName
    {
        get => _className;
        set => SetField(ref _className, value ?? string.Empty);
    }

    public string Variant
    {
        get => _variant;
        set => SetField(ref _variant, value ?? string.Empty);
    }

    public string Source { get; set; } = string.Empty;
    public string SourceBuildId { get; set; } = string.Empty;
    public int? SourceVariantIndex { get; set; }
    public string SourceUrl { get; set; } = string.Empty;
    public string LibraryContentHash { get; set; } = string.Empty;
    public int Season { get; set; }
    public BuildSeasonMode SeasonMode { get; set; }
    public BuildDifficultyMode DifficultyMode { get; set; }
    public List<BuildPurpose> Purposes { get; set; } = new();
    public List<EquipmentItemRecord> ImportedEquipment { get; set; } = new();

    [JsonIgnore]
    public string MetadataSummary => string.Join(
        " · ",
        new[]
        {
            BuildMetadata.GetSeasonLabel(SeasonMode, Season),
            BuildMetadata.GetDifficultyLabel(DifficultyMode),
            string.Join("/", Purposes
                .DefaultIfEmpty(BuildPurpose.General)
                .Select(BuildMetadata.GetPurposeLabel))
        });

    public BuildVisualFingerprint? Fingerprint { get; set; }
    public List<EquipmentAffixRule> EquipmentRules { get; set; } = new();
    public List<HudLayoutTemplate> LayoutTemplates { get; set; } = new();

    public event PropertyChangedEventHandler? PropertyChanged;

    private bool SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return false;
        }

        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        return true;
    }
}

public sealed class HudSlotLayout
{
    public EquipmentSlotKind Slot { get; set; }
    public double AnchorX { get; set; }
    public double AnchorY { get; set; }
    public double DisplayWidth { get; set; } = 180;
}

public sealed class HudLayoutTemplate
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public int ClientWidth { get; set; }
    public int ClientHeight { get; set; }
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
    public List<HudSlotLayout> Slots { get; set; } = new();

    [JsonIgnore]
    public string ResolutionLabel => $"{ClientWidth} x {ClientHeight}";
}

public static class HudLayoutTemplateService
{
    public static HudLayoutTemplate? FindExact(BuildProfile profile, int clientWidth, int clientHeight) =>
        profile.LayoutTemplates.FirstOrDefault(template =>
            template.ClientWidth == clientWidth && template.ClientHeight == clientHeight);

    public static HudLayoutTemplate Capture(
        BuildProfile profile,
        int clientWidth,
        int clientHeight,
        DateTimeOffset? updatedAt = null)
    {
        var template = FindExact(profile, clientWidth, clientHeight) ?? new HudLayoutTemplate
        {
            ClientWidth = clientWidth,
            ClientHeight = clientHeight
        };
        template.Slots = profile.EquipmentRules
            .Select(rule => new HudSlotLayout
            {
                Slot = rule.Slot,
                AnchorX = rule.AnchorX,
                AnchorY = rule.AnchorY,
                DisplayWidth = rule.DisplayWidth
            })
            .ToList();
        template.UpdatedAt = updatedAt ?? DateTimeOffset.UtcNow;

        if (!profile.LayoutTemplates.Contains(template))
        {
            profile.LayoutTemplates.Add(template);
        }

        return template;
    }

    public static bool Apply(BuildProfile profile, int clientWidth, int clientHeight)
    {
        var template = FindExact(profile, clientWidth, clientHeight);
        if (template is null)
        {
            return false;
        }

        foreach (var rule in profile.EquipmentRules)
        {
            var layout = template.Slots.FirstOrDefault(candidate => candidate.Slot == rule.Slot);
            if (layout is null)
            {
                continue;
            }

            rule.AnchorX = layout.AnchorX;
            rule.AnchorY = layout.AnchorY;
            rule.DisplayWidth = layout.DisplayWidth;
        }

        return true;
    }
}

public readonly record struct NormalizedRect(double X, double Y, double Width, double Height)
{
    public static NormalizedRect Clamp(NormalizedRect value)
    {
        var x = Math.Clamp(value.X, 0, 1);
        var y = Math.Clamp(value.Y, 0, 1);
        return new NormalizedRect(
            x,
            y,
            Math.Clamp(value.Width, 0, 1 - x),
            Math.Clamp(value.Height, 0, 1 - y));
    }
}

public sealed class PixelFrame
{
    public PixelFrame(int width, int height, byte[] bgraPixels)
    {
        if (width <= 0 || height <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(width), "Frame dimensions must be positive.");
        }

        if (bgraPixels.Length != width * height * 4)
        {
            throw new ArgumentException("BGRA pixel data length does not match the frame dimensions.", nameof(bgraPixels));
        }

        Width = width;
        Height = height;
        Pixels = bgraPixels;
    }

    public int Width { get; }
    public int Height { get; }
    public byte[] Pixels { get; }

    public byte GetLuminance(int x, int y)
    {
        x = Math.Clamp(x, 0, Width - 1);
        y = Math.Clamp(y, 0, Height - 1);
        var offset = ((y * Width) + x) * 4;
        var blue = Pixels[offset];
        var green = Pixels[offset + 1];
        var red = Pixels[offset + 2];
        return (byte)((red * 77 + green * 150 + blue * 29) >> 8);
    }
}

public readonly record struct PanelDetection(NormalizedRect Bounds, double Confidence, double BoundaryX);

public static class HudLayoutMetrics
{
    public const double DesignWidth = 695;
    public const double DesignHeight = 1060;
}

public sealed class CharacterPanelDetector
{
    private const ulong CharacterTitleHash = 0x00140C1C1E1E0200UL;
    private const double CharacterTitleMatchThreshold = 0.84;
    private static readonly double[] CharacterTitleScales = [0.85, 1.0, 1.15];
    private static readonly (double X, double Y)[] CharacterTitleAnchors =
    [
        (0.031, 0.057),
        (0.060, 0.013)
    ];

    public PanelDetection Detect(PixelFrame frame)
    {
        var viewport = FindActiveViewport(frame);
        var activeHeight = viewport.Bottom - viewport.Top;
        var expectedPanelX = frame.Width - activeHeight * 0.63;
        var searchRadius = frame.Width * 0.065;
        var startX = (int)Math.Clamp(expectedPanelX - searchRadius, frame.Width * 0.50, frame.Width * 0.76);
        var endX = (int)Math.Clamp(expectedPanelX + searchRadius, startX + 8, frame.Width * 0.78);
        var yStart = viewport.Top + (int)(activeHeight * 0.03);
        var yEnd = viewport.Top + (int)(activeHeight * 0.94);
        var scores = new List<(int X, double Edge)>();

        for (var x = startX; x <= endX; x += 2)
        {
            double sum = 0;
            var samples = 0;
            for (var y = yStart; y < yEnd; y += 4)
            {
                sum += Math.Abs(frame.GetLuminance(x - 3, y) - frame.GetLuminance(x + 3, y));
                samples++;
            }

            scores.Add((x, samples == 0 ? 0 : sum / samples));
        }

        var best = scores.OrderByDescending(score => score.Edge).First();
        var ordered = scores.Select(score => score.Edge).OrderBy(value => value).ToArray();
        var median = ordered[ordered.Length / 2];
        var relativeBoundary = Math.Clamp((best.Edge / Math.Max(4, median) - 1) / 2.6, 0, 1);
        var absoluteBoundary = Math.Clamp(best.Edge / 62, 0, 1);

        var panelX = best.X / (double)frame.Width;
        var structure = MeasureStructure(
            frame,
            best.X,
            frame.Width,
            viewport.Top + (int)(activeHeight * 0.66),
            viewport.Top + (int)(activeHeight * 0.92));
        var structuralConfidence = Math.Clamp(
            relativeBoundary * 0.48
            + absoluteBoundary * 0.22
            + Math.Clamp(structure / 36, 0, 1) * 0.30,
            0,
            1);
        var hasCharacterTitle = HasCharacterTitleMarker(frame, best.X, viewport.Top, activeHeight);
        var confidence = hasCharacterTitle ? structuralConfidence : 0;

        return new PanelDetection(
            new NormalizedRect(
                panelX,
                viewport.Top / (double)frame.Height,
                1 - panelX,
                activeHeight / (double)frame.Height),
            confidence,
            panelX);
    }

    private static bool HasCharacterTitleMarker(PixelFrame frame, int panelLeft, int activeTop, int activeHeight)
    {
        if (frame.Width < 600 || activeHeight < 320)
        {
            return false;
        }

        var expectedWidth = activeHeight * 0.064;
        var expectedHeight = activeHeight * 0.026;
        var searchRadiusX = Math.Max(2, (int)Math.Round(activeHeight * 0.016));
        var searchRadiusY = Math.Max(2, (int)Math.Round(activeHeight * 0.014));
        var searchStep = Math.Max(1, activeHeight / 420);

        foreach (var anchor in CharacterTitleAnchors)
        {
            var expectedLeft = panelLeft + activeHeight * anchor.X;
            var expectedTop = activeTop + activeHeight * anchor.Y;
            foreach (var scaleX in CharacterTitleScales)
            {
                foreach (var scaleY in CharacterTitleScales)
                {
                    var width = Math.Max(12, (int)Math.Round(expectedWidth * scaleX));
                    var height = Math.Max(8, (int)Math.Round(expectedHeight * scaleY));
                    for (var offsetY = -searchRadiusY; offsetY <= searchRadiusY; offsetY += searchStep)
                    {
                        for (var offsetX = -searchRadiusX; offsetX <= searchRadiusX; offsetX += searchStep)
                        {
                            var left = (int)Math.Round(expectedLeft + offsetX - (width - expectedWidth) / 2);
                            var top = (int)Math.Round(expectedTop + offsetY - (height - expectedHeight) / 2);
                            var match = MeasureCharacterTitleMatch(frame, left, top, width, height);
                            if (match >= CharacterTitleMatchThreshold)
                            {
                                return true;
                            }
                        }
                    }
                }
            }
        }

        return false;
    }

    private static double MeasureCharacterTitleMatch(PixelFrame frame, int left, int top, int width, int height)
    {
        if (left < 0 || top < 0 || left + width > frame.Width || top + height > frame.Height)
        {
            return 0;
        }

        Span<byte> luminances = stackalloc byte[64];
        var total = 0;
        var warmSamples = 0;
        var minimum = byte.MaxValue;
        var maximum = byte.MinValue;
        for (var row = 0; row < 8; row++)
        {
            for (var column = 0; column < 8; column++)
            {
                var x = left + (int)(width * ((column + 0.5) / 8));
                var y = top + (int)(height * ((row + 0.5) / 8));
                var offset = ((y * frame.Width) + x) * 4;
                var blue = frame.Pixels[offset];
                var green = frame.Pixels[offset + 1];
                var red = frame.Pixels[offset + 2];
                var luminance = (byte)((red * 77 + green * 150 + blue * 29) >> 8);
                var index = row * 8 + column;
                luminances[index] = luminance;
                total += luminance;
                minimum = Math.Min(minimum, luminance);
                maximum = Math.Max(maximum, luminance);
                if (luminance >= 90 && red >= blue + 10 && green >= blue + 6)
                {
                    warmSamples++;
                }
            }
        }

        if (maximum - minimum < 70 || warmSamples is < 8 or > 36)
        {
            return 0;
        }

        var average = total / luminances.Length;
        ulong hash = 0;
        for (var index = 0; index < luminances.Length; index++)
        {
            if (luminances[index] >= average)
            {
                hash |= 1UL << index;
            }
        }

        return 1 - BitOperations.PopCount(hash ^ CharacterTitleHash) / 64d;
    }

    private static (int Top, int Bottom) FindActiveViewport(PixelFrame frame)
    {
        const int maximumInactiveGap = 4;
        var sampleRight = Math.Max(1, (int)(frame.Width * 0.55));
        var rowLuminance = new double[frame.Height];
        var bestTop = 0;
        var bestBottom = frame.Height;
        var bestLength = 0;
        var runTop = -1;
        var lastActive = -1;

        for (var y = 0; y < frame.Height; y++)
        {
            long luminance = 0;
            var samples = 0;
            for (var x = 0; x < sampleRight; x += 8)
            {
                luminance += frame.GetLuminance(x, y);
                samples++;
            }

            rowLuminance[y] = samples == 0 ? 0 : luminance / (double)samples;
            var isActive = rowLuminance[y] > 4;
            if (isActive)
            {
                if (runTop < 0)
                {
                    runTop = y;
                }

                lastActive = y;
                continue;
            }

            if (runTop >= 0 && y - lastActive > maximumInactiveGap)
            {
                UpdateLongestRun(runTop, lastActive + 1, ref bestTop, ref bestBottom, ref bestLength);
                runTop = -1;
                lastActive = -1;
            }
        }

        if (runTop >= 0)
        {
            UpdateLongestRun(runTop, lastActive + 1, ref bestTop, ref bestBottom, ref bestLength);
        }

        var titleSampleCount = Math.Min(12, Math.Max(0, frame.Height - 1));
        var titleLuminance = titleSampleCount == 0
            ? 0
            : rowLuminance.Skip(1).Take(titleSampleCount).Average();
        if (bestTop == 0 && titleLuminance > 220)
        {
            var titleSearchBottom = Math.Min(frame.Height, Math.Max(24, (int)(frame.Height * 0.08)));
            for (var y = titleSampleCount + 1; y < titleSearchBottom; y++)
            {
                if (rowLuminance[y] < titleLuminance - 40)
                {
                    bestTop = y;
                    bestLength = bestBottom - bestTop;
                    break;
                }
            }
        }

        return bestLength >= frame.Height * 0.45
            ? (bestTop, bestBottom)
            : (0, frame.Height);
    }

    private static void UpdateLongestRun(
        int top,
        int bottom,
        ref int bestTop,
        ref int bestBottom,
        ref int bestLength)
    {
        var length = bottom - top;
        if (length <= bestLength)
        {
            return;
        }

        bestTop = top;
        bestBottom = bottom;
        bestLength = length;
    }

    private static double MeasureStructure(PixelFrame frame, int left, int right, int top, int bottom)
    {
        double total = 0;
        var samples = 0;
        for (var y = top; y < bottom; y += 5)
        {
            for (var x = left + 8; x < right - 8; x += 5)
            {
                var horizontal = Math.Abs(frame.GetLuminance(x - 2, y) - frame.GetLuminance(x + 2, y));
                var vertical = Math.Abs(frame.GetLuminance(x, y - 2) - frame.GetLuminance(x, y + 2));
                total += Math.Max(horizontal, vertical);
                samples++;
            }
        }

        return samples == 0 ? 0 : total / samples;
    }
}

public sealed class BuildFingerprintService
{
    public BuildVisualFingerprint Capture(PixelFrame frame, PanelDetection panel)
    {
        var skillBar = GetSkillBarBounds(panel.Bounds);
        var third = skillBar.Width / 3;
        return new BuildVisualFingerprint
        {
            LeftHash = ImageHash.ComputeAverageHash(frame, new NormalizedRect(skillBar.X, skillBar.Y, third, skillBar.Height)).ToString("X16"),
            CenterHash = ImageHash.ComputeAverageHash(frame, new NormalizedRect(skillBar.X + third, skillBar.Y, third, skillBar.Height)).ToString("X16"),
            RightHash = ImageHash.ComputeAverageHash(frame, new NormalizedRect(skillBar.X + third * 2, skillBar.Y, third, skillBar.Height)).ToString("X16"),
            CapturedAt = DateTimeOffset.UtcNow
        };
    }

    public BuildMatch Recognize(PixelFrame frame, PanelDetection panel, IEnumerable<BuildProfile> profiles, double threshold)
    {
        var observed = Capture(frame, panel);
        var candidates = profiles
            .Where(profile => profile.Fingerprint?.IsComplete == true)
            .Select(profile => new BuildMatch(profile, Compare(observed, profile.Fingerprint!)))
            .OrderByDescending(match => match.Confidence)
            .ToArray();

        if (candidates.Length == 0 || candidates[0].Confidence < threshold)
        {
            return new BuildMatch(null, candidates.FirstOrDefault().Confidence);
        }

        if (candidates.Length > 1 && candidates[0].Confidence - candidates[1].Confidence < 0.04)
        {
            return new BuildMatch(null, candidates[0].Confidence);
        }

        return candidates[0];
    }

    public static NormalizedRect GetSkillBarBounds(NormalizedRect panelBounds)
    {
        var gameplayWidth = panelBounds.X;
        var width = Math.Clamp(gameplayWidth * 0.34, 0.16, 0.23);
        var center = gameplayWidth * 0.47;
        return NormalizedRect.Clamp(new NormalizedRect(center - width / 2, 0.84, width, 0.12));
    }

    private static double Compare(BuildVisualFingerprint observed, BuildVisualFingerprint expected)
    {
        if (!BuildVisualFingerprint.TryParseHash(observed.LeftHash, out var observedLeft)
            || !BuildVisualFingerprint.TryParseHash(observed.CenterHash, out var observedCenter)
            || !BuildVisualFingerprint.TryParseHash(observed.RightHash, out var observedRight)
            || !BuildVisualFingerprint.TryParseHash(expected.LeftHash, out var expectedLeft)
            || !BuildVisualFingerprint.TryParseHash(expected.CenterHash, out var expectedCenter)
            || !BuildVisualFingerprint.TryParseHash(expected.RightHash, out var expectedRight))
        {
            return 0;
        }

        var distance = BitOperations.PopCount(observedLeft ^ expectedLeft)
            + BitOperations.PopCount(observedCenter ^ expectedCenter)
            + BitOperations.PopCount(observedRight ^ expectedRight);
        return 1 - distance / 192d;
    }
}

public readonly record struct BuildMatch(BuildProfile? Profile, double Confidence);

public static class ImageHash
{
    public static ulong ComputeAverageHash(PixelFrame frame, NormalizedRect normalizedRegion)
    {
        var region = NormalizedRect.Clamp(normalizedRegion);
        Span<byte> samples = stackalloc byte[64];
        var total = 0;
        for (var row = 0; row < 8; row++)
        {
            for (var column = 0; column < 8; column++)
            {
                var x = (int)((region.X + region.Width * ((column + 0.5) / 8)) * frame.Width);
                var y = (int)((region.Y + region.Height * ((row + 0.5) / 8)) * frame.Height);
                var luminance = frame.GetLuminance(x, y);
                samples[row * 8 + column] = luminance;
                total += luminance;
            }
        }

        var average = total / 64;
        ulong hash = 0;
        for (var index = 0; index < samples.Length; index++)
        {
            if (samples[index] >= average)
            {
                hash |= 1UL << index;
            }
        }

        return hash;
    }
}

public static class HudProfileFactory
{
    public static BuildProfile CreateStarterProfile()
    {
        return new BuildProfile
        {
            Name = "我的当前 BD",
            ClassName = "待识别职业",
            Variant = "角色面板",
            EquipmentRules = new List<EquipmentAffixRule>
            {
                Rule(EquipmentSlotKind.Helm, "头盔", 193, 83, 200, "冷却时间缩减 · 护甲", "生命上限"),
                Rule(EquipmentSlotKind.Chest, "胸甲", 196, 177, 200, "伤害减免 · 生命上限", "护甲"),
                Rule(EquipmentSlotKind.Gloves, "手套", 196, 272, 200, "暴击几率 · 攻击速度", "核心技能等级"),
                Rule(EquipmentSlotKind.Pants, "裤子", 195, 378, 200, "伤害减免 · 生命上限", "护甲"),
                Rule(EquipmentSlotKind.Boots, "靴子", 195, 480, 200, "移动速度 · 资源消耗降低", "抗性"),
                Rule(EquipmentSlotKind.Ranged, "远程武器", 195, 582, 200, "主属性 · 暴击伤害", "易伤伤害"),
                Rule(EquipmentSlotKind.MainHand, "主手", 365, 578, 95, "主属性 · 暴击伤害", "易伤伤害"),
                Rule(EquipmentSlotKind.Amulet, "项链", 540, 185, 100, "被动技能等级 · 冷却时间缩减", "移动速度"),
                Rule(EquipmentSlotKind.RingLeft, "戒指 1", 539, 275, 100, "暴击几率 · 攻击速度", "资源生成"),
                Rule(EquipmentSlotKind.RingRight, "戒指 2", 537, 373, 100, "暴击几率 · 暴击伤害", "资源生成"),
                Rule(EquipmentSlotKind.OffHand, "副手", 441, 578, 100, "主属性 · 生命上限", "资源生成")
            }
        };
    }

    public static List<EquipmentAffixRule> CreateDefaultRules(string? className = null)
    {
        var standardRules = CreateStarterProfile().EquipmentRules;
        if (!D2CoreProfileMapper.IsBarbarianClass(className))
        {
            return standardRules;
        }

        standardRules.RemoveAll(rule => rule.Slot is EquipmentSlotKind.Ranged
            or EquipmentSlotKind.MainHand
            or EquipmentSlotKind.OffHand);
        standardRules.AddRange(
        [
            Rule(EquipmentSlotKind.BarbarianBludgeoning, "双手钝击武器", 195, 582, 160, "主属性 · 暴击伤害", "易伤伤害"),
            Rule(EquipmentSlotKind.BarbarianDualWieldMainHand, "双持主手", 365, 578, 72, "主属性 · 暴击伤害", "易伤伤害"),
            Rule(EquipmentSlotKind.BarbarianSlashing, "双手挥砍武器", 441, 578, 76, "主属性 · 暴击伤害", "易伤伤害"),
            Rule(EquipmentSlotKind.BarbarianDualWieldOffHand, "双持副手", 540, 578, 100, "主属性 · 生命上限", "资源生成")
        ]);
        return standardRules;
    }

    public static EquipmentAffixRule CreateImportedRule(
        EquipmentSlotKind slot,
        string label,
        EquipmentItemRecord item,
        EquipmentAffixRule layout) => new()
        {
            Slot = slot,
            SlotLabel = label,
            ItemName = item.DisplayName,
            MandatoryText = string.Join(Environment.NewLine, item.Affixes.Select(affix => affix.DisplayText)),
            OptionalText = string.IsNullOrWhiteSpace(item.AspectName) || item.AspectName == item.DisplayName
                ? string.Empty
                : $"威能 · {item.AspectName}",
            Affixes = item.Affixes,
            IsEnabled = true,
            AnchorX = layout.AnchorX,
            AnchorY = layout.AnchorY,
            DisplayWidth = layout.DisplayWidth
        };

    public static void EnsureRules(BuildProfile profile)
    {
        profile.EquipmentRules ??= new List<EquipmentAffixRule>();
        profile.ImportedEquipment ??= new List<EquipmentItemRecord>();
        profile.LayoutTemplates ??= new List<HudLayoutTemplate>();
        profile.Purposes ??= new List<BuildPurpose>();
        if (string.Equals(profile.Source, "d2core", StringComparison.OrdinalIgnoreCase))
        {
            if (profile.Season <= 0)
            {
                var match = Regex.Match(profile.Variant ?? string.Empty, @"\bS(?<season>\d+)\b", RegexOptions.IgnoreCase);
                if (match.Success && int.TryParse(match.Groups["season"].Value, out var season))
                {
                    profile.Season = season;
                }
            }

            if (profile.SeasonMode == BuildSeasonMode.Unknown && profile.Season > 0)
            {
                profile.SeasonMode = BuildSeasonMode.Seasonal;
            }

            if (profile.Purposes.Count == 0)
            {
                profile.Purposes = BuildMetadata.ClassifyPurposes(profile.Name).ToList();
            }
        }

        var defaults = CreateDefaultRules(profile.ClassName);
        MigrateLegacyBarbarianWeaponRules(profile, defaults);
        if (!string.Equals(profile.Source, "d2core", StringComparison.OrdinalIgnoreCase))
        {
            foreach (var missing in defaults.Where(defaultRule => profile.EquipmentRules.All(rule => rule.Slot != defaultRule.Slot)))
            {
                profile.EquipmentRules.Add(missing);
            }
        }
        else
        {
            AddMissingImportedRules(profile, defaults);
        }

        foreach (var rule in profile.EquipmentRules)
        {
            var layout = defaults.FirstOrDefault(defaultRule => defaultRule.Slot == rule.Slot);
            var missingAnchor = !double.IsFinite(rule.AnchorX)
                || !double.IsFinite(rule.AnchorY)
                || (rule.AnchorX == 0 && rule.AnchorY == 0);
            if (layout is not null && missingAnchor)
            {
                rule.AnchorX = layout.AnchorX;
                rule.AnchorY = layout.AnchorY;
            }

            if (layout is not null && (!double.IsFinite(rule.DisplayWidth) || rule.DisplayWidth <= 0))
            {
                rule.DisplayWidth = layout.DisplayWidth;
            }

            rule.Affixes ??= new List<ItemAffixRecord>();
            if (rule.Affixes.Count == 0
                && string.Equals(profile.Source, "d2core", StringComparison.OrdinalIgnoreCase))
            {
                var imported = profile.ImportedEquipment.FirstOrDefault(item =>
                    D2CoreProfileMapper.TryGetSlot(profile.ClassName, item.SourceSlot, out var slot) && slot == rule.Slot);
                if (imported?.Affixes is { Count: > 0 })
                {
                    rule.Affixes = imported.Affixes;
                }
            }
        }

        foreach (var template in profile.LayoutTemplates)
        {
            template.Slots ??= new List<HudSlotLayout>();
        }
    }

    public static void ResetLayout(BuildProfile profile)
    {
        var defaults = CreateDefaultRules(profile.ClassName);
        foreach (var rule in profile.EquipmentRules)
        {
            var layout = defaults.FirstOrDefault(defaultRule => defaultRule.Slot == rule.Slot);
            if (layout is null)
            {
                continue;
            }

            rule.AnchorX = layout.AnchorX;
            rule.AnchorY = layout.AnchorY;
            rule.DisplayWidth = layout.DisplayWidth;
        }
    }

    private static void AddMissingImportedRules(BuildProfile profile, IReadOnlyList<EquipmentAffixRule> defaults)
    {
        foreach (var item in profile.ImportedEquipment)
        {
            if (!D2CoreProfileMapper.TryGetSlot(profile.ClassName, item.SourceSlot, out var slot)
                || profile.EquipmentRules.Any(rule => rule.Slot == slot))
            {
                continue;
            }

            var layout = defaults.FirstOrDefault(rule => rule.Slot == slot);
            if (layout is not null)
            {
                profile.EquipmentRules.Add(CreateImportedRule(slot, layout.SlotLabel, item, layout));
            }
        }
    }

    private static void MigrateLegacyBarbarianWeaponRules(BuildProfile profile, IReadOnlyList<EquipmentAffixRule> defaults)
    {
        if (!D2CoreProfileMapper.IsBarbarianClass(profile.ClassName))
        {
            return;
        }

        var legacySlots = new Dictionary<int, EquipmentSlotKind>
        {
            [5] = EquipmentSlotKind.Ranged,
            [12] = EquipmentSlotKind.MainHand,
            [13] = EquipmentSlotKind.OffHand
        };
        foreach (var item in profile.ImportedEquipment)
        {
            if (!legacySlots.TryGetValue(item.SourceSlot, out var legacySlot)
                || !D2CoreProfileMapper.TryGetSlot(profile.ClassName, item.SourceSlot, out var mappedSlot)
                || profile.EquipmentRules.Any(rule => rule.Slot == mappedSlot))
            {
                continue;
            }

            var legacyRule = profile.EquipmentRules.FirstOrDefault(rule => rule.Slot == legacySlot);
            var layout = defaults.FirstOrDefault(rule => rule.Slot == mappedSlot);
            if (legacyRule is null || layout is null)
            {
                continue;
            }

            legacyRule.Slot = mappedSlot;
            legacyRule.SlotLabel = layout.SlotLabel;
            legacyRule.AnchorX = layout.AnchorX;
            legacyRule.AnchorY = layout.AnchorY;
            legacyRule.DisplayWidth = layout.DisplayWidth;
            foreach (var template in profile.LayoutTemplates)
            {
                var legacyLayout = template.Slots.FirstOrDefault(slot => slot.Slot == legacySlot);
                if (legacyLayout is not null)
                {
                    legacyLayout.Slot = mappedSlot;
                }
            }
        }
    }

    private static EquipmentAffixRule Rule(
        EquipmentSlotKind slot,
        string label,
        double x,
        double y,
        double width,
        string mandatory,
        string optional) => new()
        {
            Slot = slot,
            SlotLabel = label,
            AnchorX = x,
            AnchorY = y,
            DisplayWidth = width,
            MandatoryText = mandatory,
            OptionalText = optional
        };
}
