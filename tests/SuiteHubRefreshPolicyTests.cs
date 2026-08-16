using System.Collections.Generic;
using ErenshorSuiteHub;

internal static class SuiteHubRefreshPolicyTests
{
    internal static int RunAll()
    {
        int a = 0;

        SuiteModuleDefinition journal = SuiteModuleCatalog.Find("journal");
        SuiteModuleDefinition pvp = SuiteModuleCatalog.Find("pvp");
        List<ModPresence> rows = new List<ModPresence>
        {
            new ModPresence(journal, true),
            new ModPresence(pvp, true)
        };

        SuiteHubView view = new SuiteHubView();
        int navBefore = SuiteHubRefreshPolicy.ComputeNavStructureSignature(rows);
        view.Select("journal");
        int navJournal = SuiteHubRefreshPolicy.ComputeNavStructureSignature(rows);
        view.Select("pvp");
        int navPvp = SuiteHubRefreshPolicy.ComputeNavStructureSignature(rows);
        a += TestAssert.Equal(navBefore, navJournal, "nav structure ignores journal selection");
        a += TestAssert.Equal(navJournal, navPvp, "selecting a different module does not change nav structure");

        rows.Add(new ModPresence(SuiteModuleCatalog.Find("crafting"), true));
        int navAdded = SuiteHubRefreshPolicy.ComputeNavStructureSignature(rows);
        a += TestAssert.True(navAdded != navBefore, "adding rendered nav row changes structure");

        List<ModPresence> reordered = new List<ModPresence>
        {
            new ModPresence(pvp, true),
            new ModPresence(journal, true)
        };
        int navReordered = SuiteHubRefreshPolicy.ComputeNavStructureSignature(reordered);
        a += TestAssert.True(navReordered != navBefore, "reordering rendered nav rows changes structure");

        List<ModPresence> removed = new List<ModPresence> { new ModPresence(journal, true) };
        a += TestAssert.True(SuiteHubRefreshPolicy.ComputeNavStructureSignature(removed) != navBefore,
            "removing rendered nav row changes structure");

        SuiteModuleDescriptor descriptor = Descriptor("openPanel");
        List<SuiteSettingDescriptor> basic = new List<SuiteSettingDescriptor>
        {
            BoolSetting("enabled", "Enabled", "true"),
            ChoiceSetting("mode", "Mode", "Auto", "Auto", "Manual")
        };
        List<SuiteSettingDescriptor> advanced = new List<SuiteSettingDescriptor>();
        List<SuiteSettingDescriptor> developer = new List<SuiteSettingDescriptor>();

        int pageBefore = PageSig(descriptor, basic, advanced, developer);

        descriptor.Status = "Changed status";
        descriptor.Warning = "Changed warning";
        int statusChanged = PageSig(descriptor, basic, advanced, developer);
        a += TestAssert.Equal(pageBefore, statusChanged, "status/warning changes are dynamic, not structural");

        basic[0].Value = "false";
        int boolChanged = PageSig(descriptor, basic, advanced, developer);
        a += TestAssert.Equal(pageBefore, boolChanged, "bool value change is dynamic, not structural");

        basic[1].Value = "Manual";
        int choiceChanged = PageSig(descriptor, basic, advanced, developer);
        a += TestAssert.Equal(pageBefore, choiceChanged, "choice value change is dynamic, not structural");

        basic.Add(BoolSetting("launcher", "Show launcher", "false"));
        int settingAdded = PageSig(descriptor, basic, advanced, developer);
        a += TestAssert.True(settingAdded != pageBefore, "adding setting changes page structure");
        basic.RemoveAt(basic.Count - 1);

        SuiteSettingDescriptor removedSetting = basic[1];
        basic.RemoveAt(1);
        a += TestAssert.True(PageSig(descriptor, basic, advanced, developer) != pageBefore,
            "removing setting changes page structure");
        basic.Add(removedSetting);

        descriptor.Actions.Add("closePanel");
        int actionAdded = PageSig(descriptor, basic, advanced, developer);
        a += TestAssert.True(actionAdded != pageBefore, "adding action changes page structure");
        descriptor.Actions.Remove("closePanel");

        string oldLabel = basic[0].Label;
        basic[0].Label = "Enabled now";
        a += TestAssert.True(PageSig(descriptor, basic, advanced, developer) != pageBefore,
            "setting label changes page structure");
        basic[0].Label = oldLabel;

        bool oldMutable = basic[0].Mutable;
        basic[0].Mutable = !oldMutable;
        a += TestAssert.True(PageSig(descriptor, basic, advanced, developer) != pageBefore,
            "setting mutability changes page structure");
        basic[0].Mutable = oldMutable;

        basic[1].Options.Add("Off");
        a += TestAssert.True(PageSig(descriptor, basic, advanced, developer) != pageBefore,
            "choice options change page structure");
        basic[1].Options.RemoveAt(basic[1].Options.Count - 1);

        SuiteSettingTier oldTier = basic[0].Tier;
        basic[0].Tier = SuiteSettingTier.Advanced;
        a += TestAssert.True(PageSig(descriptor, basic, advanced, developer) != pageBefore,
            "setting tier changes page structure");
        basic[0].Tier = oldTier;

        SuiteSettingKind oldKind = basic[0].Kind;
        basic[0].Kind = SuiteSettingKind.Text;
        a += TestAssert.True(PageSig(descriptor, basic, advanced, developer) != pageBefore,
            "setting kind changes page structure");
        basic[0].Kind = oldKind;

        int disclosureChanged = SuiteHubRefreshPolicy.ComputePageStructureSignature(
            "journal", true, descriptor.Actions, basic, advanced, developer, true, false, false);
        a += TestAssert.True(disclosureChanged != pageBefore, "disclosure state changes page structure");

        int developerAvailabilityChanged = SuiteHubRefreshPolicy.ComputePageStructureSignature(
            "journal", true, descriptor.Actions, basic, advanced, developer, false, false, true);
        a += TestAssert.True(developerAvailabilityChanged != pageBefore,
            "global developer UI availability changes page structure");

        return a;
    }

    private static int PageSig(SuiteModuleDescriptor descriptor,
        List<SuiteSettingDescriptor> basic, List<SuiteSettingDescriptor> advanced,
        List<SuiteSettingDescriptor> developer)
    {
        return SuiteHubRefreshPolicy.ComputePageStructureSignature(
            "journal", descriptor != null, descriptor == null ? null : descriptor.Actions,
            basic, advanced, developer, false, false, false);
    }

    private static SuiteModuleDescriptor Descriptor(params string[] actions)
    {
        SuiteModuleDescriptor d = new SuiteModuleDescriptor();
        d.ProtocolVersion = 1;
        d.ModuleId = "journal";
        d.DisplayName = "Journal";
        d.Version = "1";
        d.Status = "Ready";
        d.Warning = string.Empty;
        for (int i = 0; i < actions.Length; i++) d.Actions.Add(actions[i]);
        return d;
    }

    private static SuiteSettingDescriptor BoolSetting(string id, string label, string value)
    {
        return new SuiteSettingDescriptor
        {
            Id = id,
            Label = label,
            Tier = SuiteSettingTier.Basic,
            Kind = SuiteSettingKind.Bool,
            Value = value,
            Mutable = true
        };
    }

    private static SuiteSettingDescriptor ChoiceSetting(string id, string label, string value, params string[] options)
    {
        SuiteSettingDescriptor s = new SuiteSettingDescriptor
        {
            Id = id,
            Label = label,
            Tier = SuiteSettingTier.Basic,
            Kind = SuiteSettingKind.Choice,
            Value = value,
            Mutable = true
        };
        for (int i = 0; i < options.Length; i++) s.Options.Add(options[i]);
        return s;
    }
}
