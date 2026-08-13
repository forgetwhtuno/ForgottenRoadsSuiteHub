using System;

internal static class TestAssert
{
    internal static int True(bool value, string label)
    {
        if (!value) throw new Exception(label);
        return 1;
    }

    internal static int False(bool value, string label) { return True(!value, label); }

    internal static int Equal<T>(T expected, T actual, string label)
    {
        if (!object.Equals(expected, actual))
            throw new Exception(label + " expected=" + expected + " actual=" + actual);
        return 1;
    }
}
