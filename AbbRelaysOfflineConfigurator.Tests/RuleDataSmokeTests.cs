using AbbRelaysOfflineConfigurator.Models;
using AbbRelaysOfflineConfigurator.Services;
using AbbRelaysOfflineConfigurator.ViewModels;

namespace AbbRelaysOfflineConfigurator.Tests;

public sealed class RuleDataSmokeTests
{
    [Fact]
    public void Rex615Rules_LoadExpectedCurrentCatalog()
    {
        var rules = new ProductRuleLoader().Load(Path.Combine(
            FindRepositoryRoot(),
            "AbbRelaysOfflineConfigurator",
            "Data",
            "REX615_ROL.xml"));

        var groups = rules.MainGroups.Concat(rules.OptionGroups).ToList();
        var options = groups.SelectMany(group => group.Options).ToList();

        Assert.Equal(16, groups.Count);
        Assert.Equal(80, options.Count);
        Assert.Contains(rules.MainGroups, group => group.Name == "产品版本");
        Assert.Contains(rules.OptionGroups, group => group.Name == "版本");
        Assert.Contains(rules.OptionGroups, group => group.Name == "应用包");
        Assert.True(rules.SlotConstraintsByVersion.ContainsKey("PCL2"));
        Assert.True(rules.SlotConstraintsByVersion.ContainsKey("PCL3"));
    }

    [Fact]
    public void Rex615Slots_PreferX115AndLeaveX105EmptyForTwoBio5Modules()
    {
        var rules = new ProductRuleLoader().Load(Path.Combine(
            FindRepositoryRoot(),
            "AbbRelaysOfflineConfigurator",
            "Data",
            "REX615_ROL.xml"));
        var groups = rules.MainGroups.Concat(rules.OptionGroups).ToList();

        RuleOption Select(string groupName, string optionId) => groups
            .Single(group => group.Name == groupName)
            .Options.Single(option => option.Id == optionId);

        var selected = new[]
        {
            Select("REX615产品", "REX615"),
            Select("机箱", "B"),
            Select("产品版本", "1"),
            Select("接口级别", "0"),
            Select("选项1", "G"),
            Select("保形涂层", "N"),
            Select("应用包", "APP1"),
            Select("应用包", "APP2"),
            Select("应用包", "APP3"),
            Select("应用包", "APP5"),
            Select("开关量模块", "2x BIO5"),
            Select("模拟量模块", "SIM5"),
            Select("通讯模块", "COM1"),
            Select("通讯规约", "CMP2"),
            Select("LHMI面板", "HMI1"),
            Select("电源模块", "PSM3"),
            Select("版本", "PCL2"),
            Select("信号端子", "SCT1")
        };

        var result = new SelectionValidator(rules).Validate(selected);

        Assert.True(result.IsValid, string.Join(Environment.NewLine, result.Messages));
        Assert.False(result.SlotAssignments.Single(slot => slot.SlotId == "X105").IsAssigned);
        Assert.Equal("BIO5", result.SlotAssignments.Single(slot => slot.SlotId == "X110").Code);
        Assert.Equal("BIO5", result.SlotAssignments.Single(slot => slot.SlotId == "X115").Code);
        Assert.Equal("SIM5", result.SlotAssignments.Single(slot => slot.SlotId == "X130").Code);
        Assert.Equal(
            ["BIO5", "BIO5", "SIM5"],
            result.SlotAssignments
                .Where(slot => slot.IsHardware && slot.IsAssigned)
                .OrderBy(slot => slot.CodeOrder)
                .ThenBy(slot => slot.SlotId, StringComparer.OrdinalIgnoreCase)
                .Select(slot => slot.Code)
                .ToArray());
    }

    [Fact]
    public void Rex615Configurator_PreservesCanonicalFullCodeWhenApplyingAssignmentPriority()
    {
        const string expectedCode = "REX615B10GN+APP1+APP2+APP3+APP5+BIO5+BIO5+SIM5+COM1+CMP2+HMI1+PSM3+PCL2+SCT1";
        var viewModel = new ConfiguratorViewModel();

        void SelectOnly(string groupName, params string[] optionIds)
        {
            var selectedIds = optionIds.ToHashSet(StringComparer.OrdinalIgnoreCase);
            var group = viewModel.MainGroups.Concat(viewModel.OptionGroups)
                .Single(candidate => candidate.Name == groupName);

            foreach (var optionId in optionIds)
            {
                group.Options.Single(option => option.Id == optionId).IsSelected = true;
            }

            foreach (var option in group.Options.Where(option => !selectedIds.Contains(option.Id) && option.IsSelected))
            {
                option.IsSelected = false;
            }
        }

        SelectOnly("版本", "PCL2");
        SelectOnly("REX615产品", "REX615");
        SelectOnly("机箱", "B");
        SelectOnly("产品版本", "1");
        SelectOnly("接口级别", "0");
        SelectOnly("选项1", "G");
        SelectOnly("保形涂层", "N");
        SelectOnly("应用包", "APP1", "APP2", "APP3", "APP5");
        SelectOnly("开关量模块", "2x BIO5");
        SelectOnly("模拟量模块", "SIM5");
        SelectOnly("RTD模块");
        SelectOnly("通讯模块", "COM1");
        SelectOnly("通讯规约", "CMP2");
        SelectOnly("LHMI面板", "HMI1");
        SelectOnly("电源模块", "PSM3");
        SelectOnly("信号端子", "SCT1");
        viewModel.Recalculate();

        Assert.True(viewModel.IsCombinationValid, string.Join(Environment.NewLine, viewModel.Messages.Select(message => message.Text)));
        Assert.Equal(expectedCode, viewModel.FullCode);
        Assert.False(viewModel.Slots.Single(slot => slot.SlotId == "X105").IsAssigned);
        Assert.Equal("BIO5", viewModel.Slots.Single(slot => slot.SlotId == "X115").Code);
    }

    [Fact]
    public void Rex615Slots_KeepCanonicalCodeOrderForMixedWideHousingModules()
    {
        var rules = new ProductRuleLoader().Load(Path.Combine(
            FindRepositoryRoot(),
            "AbbRelaysOfflineConfigurator",
            "Data",
            "REX615_ROL.xml"));
        var groups = rules.MainGroups.Concat(rules.OptionGroups).ToList();

        RuleOption Select(string groupName, string optionId) => groups
            .Single(group => group.Name == groupName)
            .Options.Single(option => option.Id == optionId);

        var selected = new[]
        {
            Select("REX615产品", "REX615"),
            Select("机箱", "B"),
            Select("产品版本", "1"),
            Select("接口级别", "0"),
            Select("选项1", "G"),
            Select("保形涂层", "N"),
            Select("开关量模块", "1x BIO5"),
            Select("模拟量模块", "AIM5"),
            Select("模拟量模块", "AIM6"),
            Select("RTD模块", "2x RTD3"),
            Select("通讯模块", "COM1"),
            Select("通讯规约", "CMP2"),
            Select("LHMI面板", "HMI1"),
            Select("电源模块", "PSM3"),
            Select("版本", "PCL1"),
            Select("信号端子", "SCT1")
        };

        var result = new SelectionValidator(rules).Validate(selected);

        Assert.True(result.IsValid, string.Join(Environment.NewLine, result.Messages));
        Assert.Equal(
            ["RTD3", "BIO5", "AIM5", "AIM6", "RTD3"],
            result.SlotAssignments
                .Where(slot => slot.IsHardware && slot.IsAssigned)
                .OrderBy(slot => slot.CodeOrder)
                .ThenBy(slot => slot.SlotId, StringComparer.OrdinalIgnoreCase)
                .Select(slot => slot.Code)
                .ToArray());
        Assert.Equal("RTD3", result.SlotAssignments.Single(slot => slot.SlotId == "X110").Code);
        Assert.Equal("BIO5", result.SlotAssignments.Single(slot => slot.SlotId == "X115").Code);
        Assert.Equal("AIM5", result.SlotAssignments.Single(slot => slot.SlotId == "X120").Code);
        Assert.Equal("AIM6", result.SlotAssignments.Single(slot => slot.SlotId == "X130").Code);
        Assert.Equal("RTD3", result.SlotAssignments.Single(slot => slot.SlotId == "X105").Code);
    }

    [Fact]
    public void Rex640Rules_LoadExpectedConnectivityLevelsAndApplications()
    {
        var rules = new Rex640RuleLoader().Load();

        var productVersionGroup = Assert.Single(
            rules.MainGroups,
            group => group.Name.Equals("ProductVersion", StringComparison.OrdinalIgnoreCase));
        Assert.Equal("2", Assert.Single(productVersionGroup.Options).Id);

        var connectivityGroup = Assert.Single(
            rules.OptionGroups,
            group => group.Name.Equals("ConnectivityLevel", StringComparison.OrdinalIgnoreCase));
        Assert.Equal(["PCL5", "PCL6", "PCL7"], connectivityGroup.Options.Select(option => option.Id).ToArray());

        var applicationGroup = Assert.Single(
            rules.OptionGroups,
            group => group.Name.Equals("Application", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(applicationGroup.Options, option => option.Id == "APP51");
        Assert.Contains(applicationGroup.Options, option => option.Id == "APP53");
    }

    [Fact]
    public void Re611Rules_LoadAllCurrentDevicesAndDefaults()
    {
        var catalog = new Re611RuleLoader().Load();

        Assert.Equal(["REF611", "REM611", "REB611", "REU611"], catalog.RuleSets.Select(ruleSet => ruleSet.DeviceId).ToArray());
        Assert.All(catalog.RuleSets, ruleSet =>
        {
            Assert.NotEmpty(ruleSet.DefaultOrderCode);
            Assert.NotEmpty(ruleSet.Groups);
            Assert.NotEmpty(ruleSet.Options);
            Assert.NotEmpty(ruleSet.ValidationRules);
        });

        var ref611 = Assert.Single(catalog.RuleSets, ruleSet => ruleSet.DeviceId == "REF611");
        Assert.Equal("REF611HBAAAA1AN11G", ref611.DefaultOrderCode);
    }

    [Fact]
    public void Re630Rules_LoadCurrentVariantSet()
    {
        var catalog = new Re630RuleLoader().Load();

        Assert.Equal(14, catalog.RuleSets.Count);
        Assert.Equal(["REF630", "REG630", "REM630", "RET630"], catalog.RuleSets
            .Select(ruleSet => ruleSet.DeviceId)
            .Distinct()
            .ToArray());
        Assert.All(catalog.RuleSets, ruleSet =>
        {
            Assert.NotEmpty(ruleSet.VersionText);
            Assert.NotEmpty(ruleSet.Groups);
            Assert.NotEmpty(ruleSet.Options);
        });
        Assert.Contains(catalog.RuleSets, ruleSet => ruleSet.VersionText == "1.3.0.5");
    }

    [Fact]
    public void CnLegacyRules_LoadExpectedSeriesAndDevices()
    {
        var rules = new CnLegacySelectionRuleLoader().Load();

        Assert.Equal(2, rules.Series.Count);
        Assert.Equal(10, rules.Series.SelectMany(series => series.Devices).Count());
        Assert.Contains(rules.Series, series => series.Id == "615_CN_5_1");
        Assert.Contains(rules.Series, series => series.Id == "620_CN_2_1");
    }

    [Fact]
    public void CnLegacyRules_UseComplete615PdfCatalogInPdfOrder()
    {
        var rules = new CnLegacySelectionRuleLoader().Load();
        var series = Assert.Single(rules.Series, candidate => candidate.Id == "615_CN_5_1");
        var expectedCatalog = Expected615PdfCatalog();
        var expectedDefaults = new Dictionary<string, string>
        {
            ["REF615"] = "HCFCACABNBCZCCN11G",
            ["RED615"] = "HCDCACADABBZCAN11G",
            ["REM615"] = "HCMAACABNBAZCBN11G",
            ["REG615"] = "HCGDBDADNBAZCFN11G",
            ["RET615"] = "HCTABABANBAZCNN11G",
            ["REU615"] = "HCUAEAADNBAZCBN11G",
            ["REV615"] = "HCVBBCADNBAZCNN11G",
        };

        Assert.Equal(expectedCatalog.Keys, series.Devices.Select(device => device.Id));
        foreach (var device in series.Devices)
        {
            var expectedGroups = expectedCatalog[device.Id];
            Assert.Equal(expectedGroups.Keys, device.Groups.Select(group => group.Position));
            foreach (var group in device.Groups)
            {
                Assert.Equal(
                    expectedGroups[group.Position],
                    group.Options.Select(option => option.Code));
                Assert.Single(group.Options, option => option.IsDefault);
            }

            var defaultCode = string.Concat(device.Groups.Select(group =>
                group.Options.Single(option => option.IsDefault).Code));
            Assert.Equal(expectedDefaults[device.Id], defaultCode);
        }
    }

    [Fact]
    public void CnLegacyRules_PreserveExisting620PdfLimits()
    {
        var rules = new CnLegacySelectionRuleLoader().Load();
        var expectedGroups = new Dictionary<(string DeviceId, string Position), string[]>
        {
            [("REF620", "5-6")] = ["AA", "AB", "AC"],
            [("REF620", "17-18")] = ["1G"],
            [("REM620", "5-6")] = ["AA", "AB", "AC", "AD", "DA"],
            [("REM620", "17-18")] = ["1G"],
            [("RET620", "17-18")] = ["1G"],
        };

        foreach (var (key, expectedCodes) in expectedGroups)
        {
            var device = rules.Series.SelectMany(series => series.Devices)
                .Single(candidate => candidate.Id == key.DeviceId);
            var group = device.Groups.Single(candidate => candidate.Position == key.Position);
            Assert.Equal(expectedCodes, group.Options.Select(option => option.Code));
        }
    }

    [Fact]
    public void CnLegacyRules_KeepPdfSelectionRulesSeparateFromOriginalXmlValidation()
    {
        var rules = new CnLegacySelectionRuleLoader().Load();
        var series615 = Assert.Single(rules.Series, candidate => candidate.Id == "615_CN_5_1");
        var series620 = Assert.Single(rules.Series, candidate => candidate.Id == "620_CN_2_1");

        Assert.Equal(35, series615.Devices.Sum(device => device.ValidationBlocks.Count));
        Assert.Equal(1940, series615.Devices.Sum(device =>
            device.ValidationBlocks.Sum(block => block.Rules.Count)));
        Assert.Equal(12, series620.Devices.Sum(device => device.ValidationBlocks.Count));
        Assert.Equal(587, series620.Devices.Sum(device =>
            device.ValidationBlocks.Sum(block => block.Rules.Count)));

        Assert.Equal(168, series615.Devices.Sum(device => device.Groups.Sum(group =>
            group.Options.Sum(option => option.RequiredSelections.Count))));
        Assert.Equal(3, series615.Devices.Sum(device => device.Groups.Sum(group =>
            group.Options.Sum(option => option.ExcludedCombinedSelections.Count))));
        Assert.All(
            series620.Devices.SelectMany(device => device.Groups).SelectMany(group => group.Options),
            option =>
            {
                Assert.Empty(option.RequiredSelections);
                Assert.Empty(option.ExcludedCombinedSelections);
        });
    }

    [Fact]
    public void CnLegacyRules_TranscribeConditionalPdfNotes()
    {
        var rules = new CnLegacySelectionRuleLoader().Load();

        CnLegacyCodeOption Option(string deviceId, string position, string code) =>
            rules.Series.Single(series => series.Id == "615_CN_5_1").Devices
                .Single(device => device.Id == deviceId).Groups
                .Single(group => group.Position == position).Options
                .Single(option => option.Code == code);

        var refAd = Option("REF615", "7-8", "AD");
        Assert.Contains(refAd.RequiredSelections, requirement =>
            requirement.Position == "4" && requirement.Codes.SequenceEqual(["D", "J", "N", "Z"]));
        Assert.Contains(refAd.RequiredSelections, requirement =>
            requirement.Position == "5-6" &&
            requirement.Codes.SequenceEqual(["FE", "FF"]) &&
            Assert.Single(requirement.WhenSelections).Position == "4" &&
            requirement.WhenSelections[0].Codes.SequenceEqual(["J", "N"]));

        var redP = Option("RED615", "10", "P");
        Assert.Contains(redP.RequiredSelections, requirement =>
            requirement.Position == "4" && requirement.Codes.SequenceEqual(["D"]));
        Assert.Contains(redP.RequiredSelections, requirement =>
            requirement.Position == "9" && requirement.Codes.SequenceEqual(["N"]));
        var redIec103 = Option("RED615", "11", "D");
        Assert.Contains(redIec103.RequiredSelections, requirement =>
            requirement.Position == "9" &&
            requirement.Mode == "NoneOf" &&
            requirement.Codes.SequenceEqual(["N"]));

        var remCc = Option("REM615", "5-6", "CC");
        Assert.Contains(remCc.RequiredSelections, requirement =>
            requirement.Position == "4" && requirement.Codes.SequenceEqual(["B"]));
        Assert.Contains(remCc.RequiredSelections, requirement =>
            requirement.Position == "7-8" && requirement.Codes.SequenceEqual(["AH", "FD"]));

        var regAd = Option("REG615", "7-8", "AD");
        Assert.Contains(regAd.RequiredSelections, requirement =>
            requirement.Position == "5-6" &&
            requirement.Codes.SequenceEqual(["FE", "FF"]) &&
            Assert.Single(requirement.WhenSelections).Codes.SequenceEqual(["A", "C"]));
        Assert.Contains(regAd.RequiredSelections, requirement =>
            requirement.Position == "5-6" &&
            requirement.Codes.SequenceEqual(["BC", "BD"]) &&
            Assert.Single(requirement.WhenSelections).Codes.SequenceEqual(["D"]));

        var retBa = Option("RET615", "7-8", "BA");
        Assert.Contains(retBa.RequiredSelections, requirement =>
            requirement.Position == "4" && requirement.Codes.SequenceEqual(["A", "B", "E", "F", "Z"]));
        Assert.Contains(retBa.RequiredSelections, requirement =>
            requirement.Position == "5-6" &&
            requirement.Codes.SequenceEqual(["BE"]) &&
            Assert.Single(requirement.WhenSelections).Codes.SequenceEqual(["E", "F"]));

        var reuArc = Option("REU615", "14", "B");
        Assert.Contains(reuArc.RequiredSelections, requirement =>
            requirement.Position == "4" && requirement.Codes.SequenceEqual(["A"]));
        var reuExclusion = Assert.Single(reuArc.ExcludedCombinedSelections);
        Assert.Equal(["9", "10"], reuExclusion.Positions);
        Assert.Equal(["BB", "BN"], reuExclusion.Codes);

        var revBa = Option("REV615", "7-8", "BA");
        Assert.Contains(revBa.RequiredSelections, requirement =>
            requirement.Position == "5-6" && requirement.Codes.SequenceEqual(["BE", "BF"]));
    }

    [Fact]
    public void CnLegacySelector_UsesPdfForAvailabilityAndXmlForFinalValidation()
    {
        var viewModel = new CnLegacySelectorViewModel();
        viewModel.SelectedSeries = viewModel.Series.Single(series => series.Id == "615_CN_5_1");
        viewModel.SelectedDevice = viewModel.Devices.Single(device => device.Id == "REF615");

        var standardConfiguration = viewModel.Groups.Single(group => group.Position == "4");
        var analogInputs = viewModel.Groups.Single(group => group.Position == "5-6");
        var binaryInputs = viewModel.Groups.Single(group => group.Position == "7-8");

        standardConfiguration.SelectByCode("C");
        Assert.False(analogInputs.Options.Single(option => option.Code == "AE").IsAvailable);

        standardConfiguration.SelectByCode("J");
        Assert.True(analogInputs.Options.Single(option => option.Code == "AE").IsAvailable);
        analogInputs.SelectByCode("AE");
        Assert.False(binaryInputs.Options.Single(option => option.Code == "AD").IsAvailable);

        analogInputs.SelectByCode("FE");
        Assert.True(binaryInputs.Options.Single(option => option.Code == "AD").IsAvailable);

        var frontPanel = viewModel.Groups.Single(group => group.Position == "13");
        var englishPanel = frontPanel.Options.Single(option => option.Code == "A");
        Assert.True(englishPanel.IsAvailable);

        frontPanel.SelectByCode("A");

        Assert.True(englishPanel.IsAvailable);
        Assert.True(englishPanel.HasError);
        Assert.True(viewModel.HasErrors);
        Assert.Contains(viewModel.ValidationMessages, message =>
            message.Message.Contains("XML", StringComparison.OrdinalIgnoreCase));
    }

    private static IReadOnlyDictionary<string, IReadOnlyDictionary<string, string[]>> Expected615PdfCatalog()
    {
        var common = new Dictionary<string, string[]>
        {
            ["1"] = ["H", "1"],
            ["2"] = ["C"],
            ["9"] = ["A", "B", "C", "N"],
            ["10"] = ["A", "B", "C", "D", "E", "F", "G", "H", "N"],
            ["11"] = ["A", "B", "C", "D", "G"],
            ["12"] = ["Z"],
            ["13"] = ["A", "B", "C", "D"],
            ["16"] = ["1", "2"],
            ["17-18"] = ["1G"],
        };

        IReadOnlyDictionary<string, string[]> Device(
            string mainApplication,
            string[] standardConfigurations,
            string[] analogInputs,
            string[] binaryInputs,
            string[] option1,
            string[] option2,
            string[]? serial = null,
            string[]? ethernet = null)
        {
            var values = new Dictionary<string, string[]>(common)
            {
                ["3"] = [mainApplication],
                ["4"] = standardConfigurations,
                ["5-6"] = analogInputs,
                ["7-8"] = binaryInputs,
                ["14"] = option1,
                ["15"] = option2,
            };
            if (serial is not null)
            {
                values["9"] = serial;
            }
            if (ethernet is not null)
            {
                values["10"] = ethernet;
            }

            return values
                .OrderBy(pair => int.Parse(pair.Key.Split('-', 2)[0]))
                .ToDictionary(pair => pair.Key, pair => pair.Value);
        }

        return new Dictionary<string, IReadOnlyDictionary<string, string[]>>
        {
            ["REF615"] = Device(
                "F",
                ["C", "D", "J", "N", "Z"],
                ["AC", "AD", "FC", "FD", "AE", "AF", "FE", "FF"],
                ["AB", "AD", "FE", "AF", "FB", "AG", "FC"],
                ["A", "B", "C", "D", "E", "F", "G", "H", "J", "K", "L", "M", "P", "Q", "N"],
                ["A", "B", "C", "D", "E", "G", "H", "J", "K", "N"]),
            ["RED615"] = Device(
                "D",
                ["C", "D"],
                ["AC", "AE", "AF", "FE", "FF"],
                ["AD", "AF", "AG"],
                ["A", "D", "E", "H", "L", "M", "N"],
                ["A", "B", "C", "D", "E", "N"],
                ["A", "B", "N"],
                ["A", "B", "G", "H", "J", "K", "L", "M", "P", "Q"]),
            ["REM615"] = Device(
                "M",
                ["A", "B", "C", "Z"],
                ["AC", "AD", "AE", "AF", "AG", "AH", "CA", "CB", "CC", "CD"],
                ["AB", "AD", "FE", "AG", "FC", "AH", "AJ", "FD", "FF"],
                ["B", "N"],
                ["N"]),
            ["REG615"] = Device(
                "G",
                ["A", "C", "D"],
                ["AE", "AF", "FE", "FF", "BC", "BD", "BE", "BF"],
                ["AD", "FE", "AG", "FC", "BA", "FD"],
                ["B", "D", "F", "N"],
                ["N"]),
            ["RET615"] = Device(
                "T",
                ["A", "B", "E", "F", "Z"],
                ["BA", "BC", "BG", "BE"],
                ["BA", "BB", "FD", "FF", "AD", "FE"],
                ["B", "N"],
                ["N"]),
            ["REU615"] = Device(
                "U",
                ["A", "B"],
                ["CA", "CC", "EA"],
                ["AD", "FE", "AH", "BB"],
                ["B", "N"],
                ["N"]),
            ["REV615"] = Device(
                "V",
                ["B"],
                ["BC", "BD", "BE", "BF"],
                ["BA", "FD", "AD", "FE"],
                ["B", "D", "F", "N"],
                ["N"]),
        };
    }

    private static string FindRepositoryRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "ABBRelaysOfflineConfigurator.sln")))
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate repository root.");
    }
}
