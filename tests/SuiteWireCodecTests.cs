using System.Collections.Generic;
using ErenshorSuiteHub;

internal static class SuiteWireCodecTests
{
    internal static int RunAll()
    {
        int a = 0;
        string error;
        SuiteModuleDescriptor d = SuiteWireCodec.ParseModuleDescriptor(
            "protocol=1&module=journal&display=Journal&version=1.4.0&summary=Notes%20and%20chronicle&status=Ready&actions=openPanel,closePanel",
            "journal", out error);
        a += TestAssert.True(d != null, "valid module wire descriptor parses");
        a += TestAssert.Equal("Notes and chronicle", d.Summary, "percent decoding");
        a += TestAssert.True(d.HasAction("openPanel"), "action parsed");

        SuiteModuleDescriptor mismatch = SuiteWireCodec.ParseModuleDescriptor(
            "protocol=1&module=pvp&display=PvP&version=1&summary=x", "journal", out error);
        a += TestAssert.True(mismatch == null, "module id spoof rejected");

        SuiteModuleDescriptor duplicateAction = SuiteWireCodec.ParseModuleDescriptor(
            "protocol=1&module=journal&display=Journal&version=1&summary=x&actions=openPanel,openPanel", "journal", out error);
        a += TestAssert.True(duplicateAction == null, "duplicate action rejected");

        List<SuiteSettingDescriptor> settings = SuiteWireCodec.ParseSettings(
            "id=enabled&label=Enabled&tier=basic&type=bool&value=true&mutable=true\n" +
            "id=mode&label=Mode&tier=basic&type=text&value=Auto&mutable=false",
            SuiteSettingTier.Basic, out error);
        a += TestAssert.Equal(2, settings.Count, "two settings parse");
        a += TestAssert.True(settings[0].Mutable, "mutable flag");
        a += TestAssert.Equal(SuiteSettingKind.Bool, settings[0].Kind, "bool kind");

        List<SuiteSettingDescriptor> wrongTier = SuiteWireCodec.ParseSettings(
            "id=x&label=X&tier=developer&type=bool&value=true&mutable=true", SuiteSettingTier.Basic, out error);
        a += TestAssert.True(wrongTier == null, "tier smuggling rejected");

        List<SuiteSettingDescriptor> badBool = SuiteWireCodec.ParseSettings(
            "id=x&label=X&tier=basic&type=bool&value=maybe&mutable=true", SuiteSettingTier.Basic, out error);
        a += TestAssert.True(badBool == null, "invalid bool rejected");

        List<SuiteSettingDescriptor> choice = SuiteWireCodec.ParseSettings(
            "id=mode&label=Social%20mode&tier=basic&type=choice&value=Auto&mutable=true&options=Auto,LLM,Templates,Off",
            SuiteSettingTier.Basic, out error);
        a += TestAssert.True(choice != null, "mutable choice setting parses");
        a += TestAssert.Equal(4, choice[0].Options.Count, "choice options parsed");
        a += TestAssert.Equal("Auto", choice[0].Value, "choice value parsed");

        List<SuiteSettingDescriptor> choiceBadValue = SuiteWireCodec.ParseSettings(
            "id=mode&label=Mode&tier=basic&type=choice&value=NotAnOption&mutable=true&options=Auto,LLM",
            SuiteSettingTier.Basic, out error);
        a += TestAssert.True(choiceBadValue == null, "choice value outside options rejected");

        List<SuiteSettingDescriptor> mutableChoiceNoOptions = SuiteWireCodec.ParseSettings(
            "id=mode&label=Mode&tier=basic&type=choice&value=Auto&mutable=true",
            SuiteSettingTier.Basic, out error);
        a += TestAssert.True(mutableChoiceNoOptions == null, "mutable choice without options rejected");

        List<SuiteSettingDescriptor> readOnlyChoice = SuiteWireCodec.ParseSettings(
            "id=mode&label=Mode&tier=basic&type=choice&value=Auto&mutable=false",
            SuiteSettingTier.Basic, out error);
        a += TestAssert.True(readOnlyChoice != null, "read-only choice without options is allowed");
        return a;
    }
}
