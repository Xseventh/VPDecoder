namespace VPDecoder;

// Generated from libvpx VP9 entropy/scan tables; decoded with length guards at startup.
internal static class Vp9CoefficientProbabilities
{
    private static readonly byte[] Pareto8Full = DecodeBase64Table(
        """
        A1aABlYXWB0GVoALVypbNAlWgRFYPV5MDFaBFlhNYV0PV4EcWV1kbhFXgSFaaWd7FFiCJlt2aogXWIIrW4BskhpZgzBci2+c
        HFmDNV2TcqMfWoM6Xpx1qyJagz5eo3exJVqEQl+rergnWoRGYLF8vSpbhEtht3/CLFuET2G8gcYvXIVTYsGEyjFchVZjxYbN
        NF2FWmTJidA2XYVeZMyL0zlehmJl0I7WO16GZWbTkNg+XodpZ9aS2kBeh2xn2JTcQl+Hb2jbl95EX4dyad2Z30dgiHVq4Jvh
        SWCIeGrhneJMYYh7a+Of5E5hiH5s5aDlUGKJgW3noudSYomDbeik6FRiioZu6qbpVmKKiW/rqOpZY4qMcOyq61tjio5w7avr
        XWSLkXHurexfZIuTcu+u7WFljJVz8LDuY2WMl3Pxse5lZoyadPKz72dmjJx18rTvaWeNnnbztvBrZ42gdvO38G1ojaJ39Lnx
        b2iNpHf0uvFxaI6mePW78nJojqh59bzydGmPqnr2vvN2aY+reva/83hqj61798D0eWqPr3z3wfR7a5CxffjD9H1rkLJ9+MT0
        f2yRtH75xfWAbJG1f/nG9YJtkbeA+cf1hG2RuID5yPWGbpK6gfrJ9odukruC+sr2iW+TvYP7y/aKb5O+g/vM9oxwk8CE+833
        jXCTwYT7zvePcZTChfvP95BxlMOG+8/3knKVxYf80PiTcpXGh/zR+JVzlceI/NL4lnOVyIn80viYc5bJivzT+JlzlsqK/NT4
        m3SXzIv91fmcdJfNi/3V+Z51l86M/db5n3WXz4391/mhdpjQjv3Y+aJ2mNGO/dj5o3eZ0o/92fmkd5nTj/3Z+aZ4mdSQ/tr6
        p3iZ1JH+2/qoeZrVkv7c+ql5mtaS/tz6q3qb15P+3fqsepvYk/7d+q17m9mU/t76rnub2ZX+3vqwfJzalv7f+rF8nNuW/t/6
        sn2d3Jf+4PuzfZ3cl/7g+7R+nd2Y/uH7tX6d3Zj+4fu3f57emf7i+7h/nt+a/uL7uYCf4Jv/4/u6gJ/gm//j+7uBoOGc/+T7
        vIKg4Zz/5Pu9g6Dinf/k+76DoOKe/+T7v4Sh45//5fvAhKHjn//l+8GFouSg/+b8woWi5aD/5vzDhqPmof/n/MSGo+ah/+f8
        xYej56L/5/zGh6Pnov/n/MeIpOij/+j8yIik6KT/6PzJiaXppf/p/MmJpeml/+n8yoqm6ab/6fzLiqbppv/p/MyLpuqn/+r8
        zYum6qf/6vzOjKfrqP/r/M6Mp+uo/+v8z42o7Kn/6/zQjajsqv/r/NGOqe2r/+z80Y+p7av/7PzSkKntrP/s/NOQqe2s/+z8
        1JGq7q3/7fzVkarurf/t/NaSq++u/+391pKr767/7f3Xk6zwr//u/deTrPCv/+792JSt8LD/7v3ZlK3wsP/u/dqVrfGx/+/9
        2pWt8bL/7/3blq7xs//v/duXrvGz/+/93Jiv8rT/8P3dmK/ytP/w/d6ZsPK1//D93pmw8rX/8P3fmrHztv/w/d+asfO2//D9
        4Juy9Lf/8f3gm7L0t//x/eGcsvS4//H94Z2y9Lj/8f3inrP0uf/y/eOes/S5//L95J+09br/8v3kn7T1uv/y/eWgtfW7//L9
        5aC19bv/8v3mobb2vP/z/eaitva8//P956O39r3/8/3no7f2vf/z/eikuPe+//P96KS4977/8/3ppbn3v//0/emlufe///T9
        6qa598D/9P3qp7n3wP/0/euouvjB//T966i6+MH/9P3sqbv4wv/0/eypu/jC//T97Kq8+MP/9f3sqrz4w//1/e2rvfnE//X+
        7ay9+cT/9f7urb75xf/1/u6tvvnF//X+766/+cb/9f7vrr/5xv/1/vCvwPnH//b+8LDA+cf/9v7wscH6yP/2/vCxwfrI//b+
        8bLC+sn/9v7xssL6yf/2/vKzw/rK//b+8rTD+sr/9v7ytcT6y//3/vK1xPrL//f+87bF+8z/9/7zt8X7zP/3/vS4xvvN//f+
        9LjG+83/9/70ucf7zv/3/vS5x/vO//f+9brI+8//9/71u8j7z//3/va8yfzP//j+9rzJ/M//+P72vcr80P/4/va+yvzQ//j+
        97/L/NH/+P73v8v80f/4/vfAzPzS//j+98HM/NL/+P74ws380//4/vjCzfzT//j++MPO/NT/+f74xM781P/5/vnFz/3V//n+
        +cXP/dX/+f75xtD91v/5/vnH0f3W//n++sjS/df/+f76yNL91//5/vrJ0/3X//n++srT/df/+f76y9T92P/5/vrL1P3Y//n+
        +8zV/dn/+v77zdX92f/6/vvO1v7a//r++87X/tr/+v78z9j+2//6/vzQ2P7b//r+/NHZ/tz/+v780tn+3P/6/vzT2v7d//r+
        /NTa/t3/+v791dv+3v/6/v3V3P7e//r+/dbd/t//+v79193+3//6/v3Y3v7g//v+/dnf/uD/+/792uD+4f/7/v3b4P7h//v+
        /tzh/uH/+/7+3eL+4f/7/v7e4//i//v+/t/j/+L/+/7+4OT/4//7/v7h5f/j//v+/uLm/+T/+/7+4+b/5f/7/v/k5//m//v+
        /+Xo/+b/+/7/5un/5//8/v/n6v/n//z+/+jr/+j//P7/6ez/6P/8/v/r7f/p//z+/+zu/+r//P7/7vD/6//8///v8f/r//z+
        //Hz/+z//P7/8/X/7f/8/v/29//v//3/
        """,
        255 * 8);

    public static ReadOnlySpan<byte> Category1 => [159];

    public static ReadOnlySpan<byte> Category2 => [165, 145];

    public static ReadOnlySpan<byte> Category3 => [173, 148, 140];

    public static ReadOnlySpan<byte> Category4 => [176, 155, 140, 135];

    public static ReadOnlySpan<byte> Category5 => [180, 157, 141, 134, 130];

    public static ReadOnlySpan<byte> Category6 => [254, 254, 254, 252, 249, 243, 230, 196, 177, 153, 140, 133, 130, 129];

    public static bool TryGetPareto8Full(int pivotProbability, out ReadOnlySpan<byte> probabilities)
    {
        if (pivotProbability is < 1 or > 255)
        {
            probabilities = ReadOnlySpan<byte>.Empty;
            return false;
        }

        probabilities = Pareto8Full.AsSpan((pivotProbability - 1) * 8, 8);
        return true;
    }

    private static byte[] DecodeBase64Table(string base64, int expectedLength)
    {
        var values = Convert.FromBase64String(base64);
        if (values.Length != expectedLength)
        {
            throw new InvalidOperationException("VP9 generated coefficient probability table has an unexpected length.");
        }

        return values;
    }
}
