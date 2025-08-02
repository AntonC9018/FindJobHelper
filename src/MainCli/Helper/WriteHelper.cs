using System.Diagnostics;

namespace MainCli;

internal readonly ref struct WriteHelper
{
    public readonly Span<char> UnderlyingOutput;
    public readonly ref int CountWritten;

    public WriteHelper(Span<char> output, ref int countWritten)
    {
        UnderlyingOutput = output;
        CountWritten = ref countWritten;
    }

    public readonly Span<char> RemainingOutput
    {
        get
        {
            Debug.Assert(CountWritten <= UnderlyingOutput.Length);
            return UnderlyingOutput[CountWritten ..];
        }
    }

    public bool CanAppend(int x)
    {
        Debug.Assert(x >= 0);
        return x <= RemainingOutput.Length;
    }

    public bool Append(int num, IFormatProvider? provider)
    {
        int t;
        bool ret = num.TryFormat(
            RemainingOutput,
            out t,
            format: default,
            provider: provider);
        Debug.Assert(t <= RemainingOutput.Length);
        CountWritten += t;
        return ret;
    }

    public bool Append(string str)
    {
        bool couldWrite = str.TryCopyTo(RemainingOutput);
        if (!couldWrite)
        {
            return false;
        }

        CountWritten += str.Length;
        return true;
    }

    public bool Append(char ch)
    {
        if (CountWritten >= UnderlyingOutput.Length)
        {
            return false;
        }

        UnderlyingOutput[CountWritten] = ch;
        CountWritten++;
        return true;
    }
}
