using System.Text;

public static class CasingHelper
{
    public static void FromSnakeToPascal(ReadOnlySpan<char> s, StringBuilder output)
    {
        bool nextCapital = true;
        while (true)
        {
            if (s.Length == 0)
            {
                break;
            }
            var ch = s[0];
            switch (ch)
            {
                case '_':
                {
                    nextCapital = true;
                    break;
                }
                default:
                {
                    if (nextCapital)
                    {
                        nextCapital = false;
                        ch = char.ToUpper(ch);
                    }
                    output.Append(ch);
                    break;
                }
            }
            s = s[1..];
        }
    }
}
