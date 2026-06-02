namespace VPDecoder;

internal enum Vp9TransformType
{
    DctDct = 0,
    AdstDct = 1,
    DctAdst = 2,
    AdstAdst = 3
}

// Generated from libvpx VP9 entropy/scan tables; decoded with length guards at startup.
internal static class Vp9ScanTables
{
    private static readonly short[] DefaultScan4X4 =
    [
        0, 4, 1, 5, 8, 2, 12, 9, 3, 6, 13, 10, 7, 14, 11, 15
    ];

    private static readonly short[] RowScan4X4 =
    [
        0, 1, 4, 2, 5, 3, 6, 8, 9, 7, 12, 10, 13, 11, 14, 15
    ];

    private static readonly short[] ColScan4X4 =
    [
        0, 4, 8, 1, 12, 5, 9, 2, 13, 6, 10, 3, 7, 14, 11, 15
    ];

    private static readonly short[] DefaultScan4X4Neighbors =
    [
        0, 0, 0, 0, 0, 0, 1, 4, 4, 4, 1, 1, 8, 8, 5, 8, 2,
        2, 2, 5, 9, 12, 6, 9, 3, 6, 10, 13, 7, 10, 11, 14, 0, 0
    ];

    private static readonly short[] RowScan4X4Neighbors =
    [
        0, 0, 0, 0, 0, 0, 1, 1, 4, 4, 2, 2, 5, 5, 4, 4, 8,
        8, 6, 6, 8, 8, 9, 9, 12, 12, 10, 10, 13, 13, 14, 14, 0, 0
    ];

    private static readonly short[] ColScan4X4Neighbors =
    [
        0, 0, 0, 0, 4, 4, 0, 0, 8, 8, 1, 1, 5, 5, 1, 1, 9,
        9, 2, 2, 6, 6, 2, 2, 3, 3, 10, 10, 7, 7, 11, 11, 0, 0
    ];

    private static readonly short[] DefaultScan8X8 =
    [
        0, 8, 1, 16, 9, 2, 17, 24, 10, 3, 18, 25, 32, 11, 4, 26,
        33, 19, 40, 12, 34, 27, 5, 41, 20, 48, 13, 35, 42, 28, 21, 6,
        49, 56, 36, 43, 29, 7, 14, 50, 57, 44, 22, 37, 15, 51, 58, 30,
        45, 23, 52, 59, 38, 31, 60, 53, 46, 39, 61, 54, 47, 62, 55, 63
    ];

    private static readonly short[] RowScan8X8 =
    [
        0, 1, 2, 8, 9, 3, 16, 10, 4, 17, 11, 24, 5, 18, 25, 12,
        19, 26, 32, 6, 13, 20, 33, 27, 7, 34, 40, 21, 28, 41, 14, 35,
        48, 42, 29, 36, 49, 22, 43, 15, 56, 37, 50, 44, 30, 57, 23, 51,
        58, 45, 38, 52, 31, 59, 53, 46, 60, 39, 61, 47, 54, 55, 62, 63
    ];

    private static readonly short[] ColScan8X8 =
    [
        0, 8, 16, 1, 24, 9, 32, 17, 2, 40, 25, 10, 33, 18, 48, 3,
        26, 41, 11, 56, 19, 34, 4, 49, 27, 42, 12, 35, 20, 57, 50, 28,
        5, 43, 13, 36, 58, 51, 21, 44, 6, 29, 59, 37, 14, 52, 22, 7,
        45, 60, 30, 15, 38, 53, 23, 46, 31, 61, 39, 54, 47, 62, 55, 63
    ];

    private static readonly short[] DefaultScan8X8Neighbors =
    [
        0, 0, 0, 0, 0, 0, 8, 8, 1, 8, 1, 1, 9, 16, 16, 16, 2, 9, 2,
        2, 10, 17, 17, 24, 24, 24, 3, 10, 3, 3, 18, 25, 25, 32, 11, 18, 32, 32,
        4, 11, 26, 33, 19, 26, 4, 4, 33, 40, 12, 19, 40, 40, 5, 12, 27, 34, 34,
        41, 20, 27, 13, 20, 5, 5, 41, 48, 48, 48, 28, 35, 35, 42, 21, 28, 6, 6,
        6, 13, 42, 49, 49, 56, 36, 43, 14, 21, 29, 36, 7, 14, 43, 50, 50, 57, 22,
        29, 37, 44, 15, 22, 44, 51, 51, 58, 30, 37, 23, 30, 52, 59, 45, 52, 38, 45,
        31, 38, 53, 60, 46, 53, 39, 46, 54, 61, 47, 54, 55, 62, 0, 0
    ];

    private static readonly short[] RowScan8X8Neighbors =
    [
        0, 0, 0, 0, 1, 1, 0, 0, 8, 8, 2, 2, 8, 8, 9, 9, 3, 3, 16,
        16, 10, 10, 16, 16, 4, 4, 17, 17, 24, 24, 11, 11, 18, 18, 25, 25, 24, 24,
        5, 5, 12, 12, 19, 19, 32, 32, 26, 26, 6, 6, 33, 33, 32, 32, 20, 20, 27,
        27, 40, 40, 13, 13, 34, 34, 40, 40, 41, 41, 28, 28, 35, 35, 48, 48, 21, 21,
        42, 42, 14, 14, 48, 48, 36, 36, 49, 49, 43, 43, 29, 29, 56, 56, 22, 22, 50,
        50, 57, 57, 44, 44, 37, 37, 51, 51, 30, 30, 58, 58, 52, 52, 45, 45, 59, 59,
        38, 38, 60, 60, 46, 46, 53, 53, 54, 54, 61, 61, 62, 62, 0, 0
    ];

    private static readonly short[] ColScan8X8Neighbors =
    [
        0, 0, 0, 0, 8, 8, 0, 0, 16, 16, 1, 1, 24, 24, 9, 9, 1, 1, 32,
        32, 17, 17, 2, 2, 25, 25, 10, 10, 40, 40, 2, 2, 18, 18, 33, 33, 3, 3,
        48, 48, 11, 11, 26, 26, 3, 3, 41, 41, 19, 19, 34, 34, 4, 4, 27, 27, 12,
        12, 49, 49, 42, 42, 20, 20, 4, 4, 35, 35, 5, 5, 28, 28, 50, 50, 43, 43,
        13, 13, 36, 36, 5, 5, 21, 21, 51, 51, 29, 29, 6, 6, 44, 44, 14, 14, 6,
        6, 37, 37, 52, 52, 22, 22, 7, 7, 30, 30, 45, 45, 15, 15, 38, 38, 23, 23,
        53, 53, 31, 31, 46, 46, 39, 39, 54, 54, 47, 47, 55, 55, 0, 0
    ];

    private static readonly short[] DefaultScan16X16 = DecodeInt16Table(
        """
        AAAQAAEAIAARAAIAMAAhABIAAwBAACIAMQATAEEAUAAyAAQAIwBCABQAUQBgADMABQAkAFIAYQBDAHAAFQA0AGIAJQBTAHEABgBE
        AIAANQAWAGMAcgBUAAcAgQAmAEUAZABzAJAAggBVADYAFwAIAJEAJwBGAHQAZQCDAKAAkgA3AFYAGABHAIQAdQChACgACQBmAJMA
        sACiAFcAOAAZAIUAdgCxAJQASABnACkAowAKAMAAsgBYADkAhgCVAHcAGgCkAEkAaADBACoAswDQAAsAhwBZAKUAeACWADoAwgC0
        ABsASgDRAGkAlwCIACsAWgDgAKYAwwC1AHkA0gA7AAwAmABqAKcAxABLAIkA4QDTAPAAtgB6AFsAHADFAA0A4gCoALcAmQAsANQA
        igBrAPEAPAAdAHsAxgC4AOMAqQDyAEwA1QCaAC0AXAAOAMcAiwA9AOQA1gCqALkA8wBsAE0AmwAeAA8AyADlAHwA1wD0AF0ALgC6
        AKsAyQBtAIwA5gA+ANgA9QAfAH0ATgCcAOcALwC7AMoA2QBeAPYAjQA/AOgArABuAPcAnQBPANoAywB+AOkAvAD4AF8ArQCOANsA
        bwD5AOoAngB/AL0AzAD6AOsAjwCuANwAzQCfAPsAvgDdAK8A7ADtAL8AzgD8AN4A/QDPAO4A3wD+AO8A/wA=
        """,
        256);

    private static readonly short[] DefaultScan16X16Neighbors = DecodeInt16Table(
        """
        AAAAAAAAAAAAAAAAEAAQAAEAEAABAAEAIAAgABEAIAACABEAAgACADAAMAASACEAIQAwAAMAEgAxAEAAQABAACIAMQADAAMAEwAi
        ADIAQQAEABMAQQBQAFAAUAAjADIABAAEABQAIwBCAFEAUQBgADMAQgBgAGAABQAUACQAMwBSAGEAFQAkAEMAUgBhAHAABQAFADQA
        QwBwAHAAJQA0AAYAFQBTAGIAYgBxAEQAUwAGAAYAcQCAABYAJQA1AEQAVABjAGMAcgCAAIAAcgCBAEUAVAAmADUABwAWAAcABwCB
        AJAAFwAmADYARQBkAHMAVQBkAHMAggCQAJAAggCRACcANgBGAFUACAAXADcARgB0AIMAZQB0AJEAoAAYACcACAAIAFYAZQCDAJIA
        oACgAJIAoQBHAFYAKAA3AAkAGAB1AIQAZgB1AKEAsACEAJMAOABHAFcAZgAZACgAkwCiAAkACQCwALAAogCxAEgAVwApADgAdgCF
        AIUAlABnAHYACgAZAJQAowA5AEgAWABnALEAwAAaACkAowCyAMAAwAAKAAoAdwCGAEkAWACVAKQAaAB3AIYAlQAqADkAsgDBAKQA
        swALABoAOgBJAMEA0ABZAGgAhwCWAHgAhwAbACoASgBZANAA0ACWAKUAswDCAKUAtABpAHgAwgDRACsAOgALAAsAiACXAFoAaQCX
        AKYAtADDADsASgB5AIgA0QDgAMMA0gDgAOAApgC1AGoAeQBLAFoADAAbALUAxAAMAAwA0gDhAJgApwCnALYAiQCYABwAKwDEANMA
        egCJAFsAagDhAPAALAA7AA0AHABrAHoAtgDFAKgAtwDTAOIAmQCoAOIA8QA8AEsAxQDUAIoAmQAdACwATABbAA0ADQC3AMYAewCK
        AC0APADUAOMAxgDVAJoAqQCpALgA4wDyAFwAawA9AEwAiwCaAA4AHQAOAA4AuADHANUA5ABsAHsAxwDWAOQA8wBNAFwAHgAtAKoA
        uQCbAKoAuQDIAF0AbAB8AIsA1gDlAC4APQDIANcA5QD0AA8AHgBtAHwAPgBNAIwAmwDXAOYAHwAuAKsAugC6AMkAyQDYAE4AXQDm
        APUAfQCMAC8APgDYAOcAnACrAF4AbQDnAPYAjQCcAD8ATgDKANkAuwDKAG4AfQDZAOgArAC7AOgA9wBPAF4AnQCsAH4AjQDLANoA
        XwBuAOkA+ADaAOkAjgCdAG8AfgCtALwAvADLAOoA+QDbAOoAfwCOAJ4ArQDMANsAvQDMAI8AngDrAPoArgC9AM0A3ACfAK4A3ADr
        AN0A7ACvAL4AvgDNAOwA+wDOAN0A7QD8AL8AzgDeAO0AzwDeAO4A/QDfAO4A7wD+AAAAAAA=
        """,
        514);

    private static readonly short[] RowScan16X16 = DecodeInt16Table(
        """
        AAABAAIAEAADABEABAASACAABQAhABMABgAiADAAFAAxAAcAIwAVADIAQAAIACQAQQAWADMAJQBQAAkAQgA0ABcAJgBRAEMACgA1
        ABgAUgBEAGAAJwALADYAUwBhAEUAGQBiAFQAKABwADcADABGAGMAcQBVABoAKQA4AHIAZAANAEcAgABWABsAcwBlAIEAKgA5
        AEgAdAAOAFcAggBmAJAASQCDAHUAHAA6AA8AWAArAJEAZwCEAJIAdgBKAKAAWQCFAGgAHQA7AJMAdwAsAKEAlABaAGkAhgCi
        AHgAsABLAIcAlQAeADwAowCxAC0AeQBbAGoApACyAJYAwACIAKUAswAfAJcAwQBMAHoAPQCJAMIAawCYALQA0AAuAKYApwDD
        AFwAtQCKANEAewCZAOAAxABNAKgA0gC2APAAbADFAD4AmgDhALcAqQDTAC8AiwBdALgA4gDUAPEAxgCqAHwAmwDHAE4A1QC5
        AG0A4wDIAD8A5ADyAIwA1gCrALoAnADlAPMAfQBeAMkA9ADXANgA5gCNALsAygBPAKwAbgCdAPUA2QDnAF8A9gDoAH4AywD3
        AOkArQDaAI4AbwCeALwA+AB/AOoA2wD5AL0AzACPAK4AnwD6AOsAzQDcAK8AvgD7AN0AvwDOAOwAzwDtAPwA3gD9AN8A7gDv
        AP4A/wA=
        """,
        256);

    private static readonly short[] ColScan16X16 = DecodeInt16Table(
        """
        AAAQACAAMAABAEAAEQBQACEAYAAxAAIAQQBwABIAUQAiAIAAMgBhAAMAQgCQABMAcQAjAFIAoABiADMAgQAEAEMAsAAUAHIA
        kQBTACQAYwCCADQAwAAFAKEARABzABUAkgBUANAAsQAlAIMAZAA1AKIA4ABFAAYAdADBAJMAVQAWAPAAhAAmALIAZQCjADYA
        0QB1AEYABwCUAMIAVgCzAOEAFwCFACcApAAIAGYA0gDxADcAwwB2AJUARwC0ABgAVwDiAIYApQDTACgAZwA4AEgAlgDE
        APIAdwAJALUA4wBYAKYAGQCHACkAaADUADkAlwDFAHgASQDzALYAiACnANUAWQAKAOQAaQCYAMYAGgAqAHkAtwD0AKgA
        OgCJAOUASgDWAFoAmQDHALgACwBqAPUAGwB6AOYAqQArANcAOwDIAIoAuQD2AEsADABbAJoA2ADnAGsAHAAsAMkAewCq
        ADwA9wDoAEwAiwANAFwA2QC6APgAmwBsAB0AfAAtAMoA6QCrAD0ADgBNAIwADwD5AF0AHgC7AJwA2gAuAG0AfQA+
        AKwATgDLAB8AjQDqAF4ALwC8AD8AnQBuAPoA2wBPAH4AzACtAI4AXwC9AG8A6wCeANwA+wB/AK4AjwDNAOwAnwC+
        AN0A/ACvAM4A7QC/AP0A3gDuAM8A/gDfAO8A/wA=
        """,
        256);

    private static readonly short[] RowScan16X16Neighbors = DecodeInt16Table(
        """
        AAAAAAAAAAABAAEAAAAAAAIAAgAQABAAAwADABEAEQAQABAABAAEACAAIAASABIABQAFACEAIQAgACAAEwATADAAMAAGAAYAIgAi
        ABQAFAAxADEAMAAwAAcABwAjACMAQABAABUAFQAyADIAJAAkAEAAQAAIAAgAQQBBADMAMwAWABYAJQAlAFAAUABCAEIACQAJ
        ADQANAAXABcAUQBRAEMAQwBQAFAAJgAmAAoACgA1ADUAUgBSAGAAYABEAEQAGAAYAGEAYQBTAFMAJwAnAGAAYAA2ADYACwAL
        AEUARQBiAGIAcABwAFQAVAAZABkAKAAoADcANwBxAHEAYwBjAAwADABGAEYAcABwAFUAVQAaABoAcgByAGQAZACAAIAAKQAp
        ADgAOABHAEcAcwBzAA0ADQBWAFYAgQCBAGUAZQCAAIAASABIAIIAggB0AHQAGwAbADkAOQAOAA4AVwBXACoAKgCQAJAAZgBm
        AIMAgwCRAJEAdQB1AEkASQCQAJAAWABYAIQAhABnAGcAHAAcADoAOgCSAJIAdgB2ACsAKwCgAKAAkwCTAFkAWQBoAGgAhQCF
        AKEAoQB3AHcAoACgAEoASgCGAIYAlACUAB0AHQA7ADsAogCiALAAsAAsACwAeAB4AFoAWgBpAGkAowCjALEAsQCVAJUAsACw
        AIcAhwCkAKQAsgCyAB4AHgCWAJYAwADAAEsASwB5AHkAPAA8AIgAiADBAMEAagBqAJcAlwCzALMAwADAAC0ALQClAKUApgCm
        AMIAwgBbAFsAtAC0AIkAiQDQANAAegB6AJgAmADQANAAwwDDAEwATACnAKcA0QDRALUAtQDgAOAAawBrAMQAxAA9AD0AmQCZ
        AOAA4AC2ALYAqACoANIA0gAuAC4AigCKAFwAXAC3ALcA4QDhANMA0wDwAPAAxQDFAKkAqQB7AHsAmgCaAMYAxgBNAE0A1ADU
        ALgAuABsAGwA4gDiAMcAxwA+AD4A4wDjAPEA8QCLAIsA1QDVAKoAqgC5ALkAmwCbAOQA5ADyAPIAfAB8AF0AXQDIAMgA8wDz
        ANYA1gDXANcA5QDlAIwAjAC6ALoAyQDJAE4ATgCrAKsAbQBtAJwAnAD0APQA2ADYAOYA5gBeAF4A9QD1AOcA5wB9AH0AygDK
        APYA9gDoAOgArACsANkA2QCNAI0AbgBuAJ0AnQC7ALsA9wD3AH4AfgDpAOkA2gDaAPgA+AC8ALwAywDLAI4AjgCtAK0AngCe
        APkA+QDqAOoAzADMANsA2wCuAK4AvQC9APoA+gDcANwAvgC+AM0AzQDrAOsAzgDOAOwA7AD7APsA3QDdAPwA/ADeAN4A7QDt
        AO4A7gD9AP0A/gD+AAAAAAA=
        """,
        514);

    private static readonly short[] ColScan16X16Neighbors = DecodeInt16Table(
        """
        AAAAAAAAAAAQABAAIAAgAAAAAAAwADAAAQABAEAAQAARABEAUABQACEAIQABAAEAMQAxAGAAYAACAAIAQQBBABIAEgBwAHAA
        IgAiAFEAUQACAAIAMgAyAIAAgAADAAMAYQBhABMAEwBCAEIAkACQAFIAUgAjACMAcQBxAAMAAwAzADMAoACgAAQABABiAGIA
        gQCBAEMAQwAUABQAUwBTAHIAcgAkACQAsACwAAQABACRAJEANAA0AGMAYwAFAAUAggCCAEQARADAAMAAoQChABUAFQBzAHMA
        VABUACUAJQCSAJIA0ADQADUANQAFAAUAZABkALEAsQCDAIMARQBFAAYABgDgAOAAdAB0ABYAFgCiAKIAVQBVAJMAkwAmACYA
        wQDBAGUAZQA2ADYABgAGAIQAhACyALIARgBGAKMAowDRANEABwAHAHUAdQAXABcAlACUAAcABwBWAFYAwgDCAOEA4QAnACcA
        swCzAGYAZgCFAIUANwA3AKQApAAIAAgARwBHANIA0gB2AHYAlQCVAMMAwwAYABgAVwBXACgAKAA4ADgAhgCGALQAtADiAOIA
        ZwBnAAgACAClAKUA0wDTAEgASACWAJYACQAJAHcAdwAZABkAWABYAMQAxAApACkAhwCHALUAtQBoAGgAOQA5AOMA4wCmAKYA
        eAB4AJcAlwDFAMUASQBJAAkACQDUANQAWQBZAIgAiAC2ALYACgAKABoAGgBpAGkApwCnAOQA5ACYAJgAKgAqAHkAeQDVANUA
        OgA6AMYAxgBKAEoAiQCJALcAtwCoAKgACgAKAFoAWgDlAOUACwALAGoAagDWANYAmQCZABsAGwDHAMcAKwArALgAuAB6AHoA
        qQCpAOYA5gA7ADsACwALAEsASwCKAIoAyADIANcA1wBbAFsADAAMABwAHAC5ALkAawBrAJoAmgAsACwA5wDnANgA2AA8ADwA
        ewB7AAwADABMAEwAyQDJAKoAqgDoAOgAiwCLAFwAXAANAA0AbABsAB0AHQC6ALoA2QDZAJsAmwAtAC0ADQANAD0APQB8AHwA
        DgAOAOkA6QBNAE0ADgAOAKsAqwCMAIwAygDKAB4AHgBdAF0AbQBtAC4ALgCcAJwAPgA+ALsAuwAPAA8AfQB9ANoA2gBOAE4A
        HwAfAKwArAAvAC8AjQCNAF4AXgDqAOoAywDLAD8APwBuAG4AvAC8AJ0AnQB+AH4ATwBPAK0ArQBfAF8A2wDbAI4AjgDMAMwA
        6wDrAG8AbwCeAJ4AfwB/AL0AvQDcANwAjwCPAK4ArgDNAM0A7ADsAJ8AnwC+AL4A3QDdAK8ArwDtAO0AzgDOAN4A3gC/AL8A
        7gDuAM8AzwDfAN8A7wDvAAAAAAA=
        """,
        514);

    private static readonly short[] DefaultScan32X32 = DecodeInt16Table(
        """
        AAAgAAEAQAAhAAIAYABBACIAgAADAGEAQgCgAIEAIwBiAAQAQwCCAKEAwAAkAGMA4AAFAKIAwQBEAIMAJQBkAOEAwgAAAaMA
        RQCEAAYA4gABASABwwBlAKQAJgACAQcA4wAhAYUAQAFGAMQApQAiAQMB5AAnAEEBZgBgAQgAxQBHAIYAQgEjAQQBYQGAAeUA
        pgBnACgAYgFDASQBhwCBAcYABQFIAAkAoAGnAIIBYwHmAEQBaAAlASkAoQHHAIgABgGDAcABRQFkAQoASQCiAecAqADBASYB
        hAFpAKMBBwEqAMgAZQHCAYkA4AFKAEYB6AALAIUBqQAnAaQBagDDAeEBZgEIAUcByQArAIoAAALiAYYBKAHpAKoApQFLAMQB
        ZwEMAAECCQHjAUgBawDKAAICIAKmAYcBxQGLACwA6gDkASkBaAGrAEwAAwIhAgoBSQHGAQ0ApwHLAGwAIgLlAUACKgHrAIwA
        aQFKAawAIwItAMcBCwFBAuYBTQDMAGoBYAIOACsBQgJtAOwA5wFhAksBjQBDAi4ADwCtAGICawFOAM0AEABuAO0AYwKOAC8A
        rgBPAM4AEQBvAO4AMACPAFAArwBwAM8AMQASAO8AUQBxABMAMgBSAHIAMwBTAHMAgAIEAogBDAGQABQAoAKBAiQCBQKoAYkB
        LAENAbAAkQA0ABUAwAKhAoICRAIlAgYCyAGpAYoBTAEtAQ4B0ACxAJIAVAA1ABYA4ALBAqICgwJkAkUCJgIHAugByQGqAYsB
        bAFNAS4BDwHwANEAsgCTAHQAVQA2ABcA4QLCAqMCZQJGAicC6QHKAasBbQFOAS8B8QDSALMAdQBWADcA4gLDAmYCRwLqAcsB
        bgFPAfIA0wB2AFcA4wJnAusBbwHzAHcAAAOEAggCjAEQAZQAGAAgAwEDpAKFAigCCQKsAY0BMAERAbQAlQA4ABkAQAMhAwID
        xAKlAoYCSAIpAgoCzAGtAY4BUAExARIB1AC1AJYAWAA5ABoAYANBAyIDAwPkAsUCpgKHAmgCSQIqAgsC7AHNAa4BjwFwAVEB
        MgETAfQA1QC2AJcAeABZADoAGwBhA0IDIwPlAsYCpwJpAkoCKwLtAc4BrwFxAVIBMwH1ANYAtwB5AFoAOwBiA0MD5gLHAmoC
        SwLuAc8BcgFTAfYA1wB6AFsAYwPnAmsC7wFzAfcAewCAAwQDiAIMApABFAGYABwAoAOBAyQDBQOoAokCLAINArABkQE0ARUB
        uACZADwAHQDAA6EDggNEAyUDBgPIAqkCigJMAi0CDgLQAbEBkgFUATUBFgHYALkAmgBcAD0AHgDgA8EDogODA2QDRQMmAwcD
        6ALJAqoCiwJsAk0CLgIPAvAB0QGyAZMBdAFVATYBFwH4ANkAugCbAHwAXQA+AB8A4QPCA6MDZQNGAycD6QLKAqsCbQJOAi8C
        8QHSAbMBdQFWATcB+QDaALsAfQBeAD8A4gPDA2YDRwPqAssCbgJPAvIB0wF2AVcB+gDbAH4AXwDjA2cD6wJvAvMBdwH7AH8A
        hAMIA4wCEAKUARgBnACkA4UDKAMJA6wCjQIwAhECtAGVATgBGQG8AJ0AxAOlA4YDSAMpAwoDzAKtAo4CUAIxAhIC1AG1AZYB
        WAE5ARoB3AC9AJ4A5APFA6YDhwNoA0kDKgMLA+wCzQKuAo8CcAJRAjICEwL0AdUBtgGXAXgBWQE6ARsB/ADdAL4AnwDlA8YD
        pwNpA0oDKwPtAs4CrwJxAlICMwL1AdYBtwF5AVoBOwH9AN4AvwDmA8cDagNLA+4CzwJyAlMC9gHXAXoBWwH+AN8A5wNrA+8C
        cwL3AXsB/wCIAwwDkAIUApgBHAGoA4kDLAMNA7ACkQI0AhUCuAGZATwBHQHIA6kDigNMAy0DDgPQArECkgJUAjUCFgLYAbkB
        mgFcAT0BHgHoA8kDqgOLA2wDTQMuAw8D8ALRArICkwJ0AlUCNgIXAvgB2QG6AZsBfAFdAT4BHwHpA8oDqwNtA04DLwPxAtIC
        swJ1AlYCNwL5AdoBuwF9AV4BPwHqA8sDbgNPA/IC0wJ2AlcC+gHbAX4BXwHrA28D8wJ3AvsBfwGMAxADlAIYApwBrAONAzAD
        EQO0ApUCOAIZArwBnQHMA60DjgNQAzEDEgPUArUClgJYAjkCGgLcAb0BngHsA80DrgOPA3ADUQMyAxMD9ALVArYClwJ4AlkC
        OgIbAvwB3QG+AZ8B7QPOA68DcQNSAzMD9QLWArcCeQJaAjsC/QHeAb8B7gPPA3IDUwP2AtcCegJbAv4B3wHvA3MD9wJ7Av8B
        kAMUA5gCHAKwA5EDNAMVA7gCmQI8Ah0C0AOxA5IDVAM1AxYD2AK5ApoCXAI9Ah4C8APRA7IDkwN0A1UDNgMXA/gC2QK6ApsC
        fAJdAj4CHwLxA9IDswN1A1YDNwP5AtoCuwJ9Al4CPwLyA9MDdgNXA/oC2wJ+Al8C8wN3A/sCfwKUAxgDnAK0A5UDOAMZA7wC
        nQLUA7UDlgNYAzkDGgPcAr0CngL0A9UDtgOXA3gDWQM6AxsD/ALdAr4CnwL1A9YDtwN5A1oDOwP9At4CvwL2A9cDegNbA/4C
        3wL3A3sD/wKYAxwDuAOZAzwDHQPYA7kDmgNcAz0DHgP4A9kDugObA3wDXQM+Ax8D+QPaA7sDfQNeAz8D+gPbA34DXwP7A38D
        nAO8A50D3AO9A54D/APdA74DnwP9A94DvwP+A98D/wM=
        """,
        1024);

    private static readonly short[] DefaultScan32X32Neighbors = DecodeInt16Table(
        """
        AAAAAAAAAAAAAAAAIAAgAAEAIAABAAEAQABAACEAQAACACEAYABgAAIAAgBBAGAAIgBBAIAAgABhAIAAAwAiAEIAYQADAAMA
        IwBCAGIAgQCBAKAAoACgAAQAIwBDAGIAwADAAAQABACCAKEAoQDAACQAQwBjAIIABQAkAEQAYwDBAOAAogDBAOAA4ACDAKIA
        JQBEAGQAgwAFAAUAwgDhAOEAAAEAAQABowDCAEUAZACEAKMABgAlAOIAAQEGAAYAwwDiAAEBIAFlAIQAIAEgASYARQCkAMMA
        hQCkAAIBIQHjAAIBxADjAAcAJgAhAUABRgBlAEABQAEHAAcApQDEACcARgBmAIUAIgFBAQMBIgHkAAMBQQFgAWABYAHFAOQA
        hgClAEcAZgAIACcAQgFhASMBQgEEASMBZwCGAGEBgAGmAMUA5QAEASgARwAIAAgAgAGAAYcApgBiAYEBQwFiAcYA5QAkAUMB
        SABnAAUBJAEJACgAgQGgAacAxgBoAIcA5gAFAWMBggGgAaABJQFEAUQBYwEJAAkAKQBIAIIBoQHHAOYAiACnAKEBwAEGASUB
        ZAGDAUkAaACDAaIB5wAGAQoAKQCoAMcARQFkAaIBwQFpAIgAwAHAASoASQAmAUUByADnAAoACgBlAYQBiQCoAAcBJgGEAaMB
        SgBpAKMBwgHBAeABRgFlAegABwEnAUYBqQDIAAsAKgBqAIkA4AHgAcIB4QFmAYUBCAEnAckA6ACKAKkAhQGkASsASgCkAcMB
        RwFmAQsACwDhAQAC6QAIAcMB4gEoAUcBSwBqAKoAyQDiAQECAAIAAoYBpQFnAYYBpQHEAWsAigAMACsAygDpAMQB4wEJASgB
        SAFnAYsAqgAsAEsA4wECAgECIALqAAkBKQFIAaYBxQEMAAwAhwGmAasAygBMAGsAAgIhAsUB5AEgAiACCgEpAcsA6gBsAIsA
        SQFoASoBSQGMAKsAAwIiAg0ALACnAcYB6wAKASECQALGAeUBLQBMAKwAywBKAWkBQAJAAg0ADQALASoBIgJBAk0AbADMAOsA
        xwHmAUECYAIrAUoBbQCMACMCQgIOAC0ADgAOAI0ArABCAmECSwFqAS4ATQCtAMwADwAPAE4AbQDNAOwAQwJiAm4AjQAPAC4A
        jgCtAC8ATgCuAM0AEAAQAE8AbgDOAO0AEAAvAG8AjgAwAE8AjwCuAFAAbwCvAM4AEQAwABEAEQDPAO4AMQBQAFEAcAASABIA
        EgAxADIAUQBSAHEAEwAyADMAUgBTAHIAYAJgAuQBAwJoAYcB7AALAXAAjwATABMAgAKAAmECgAIEAiMC5QEEAogBpwFpAYgB
        DAErAe0ADAGQAK8AcQCQABQAMwAUABQAoAKgAoECoAJiAoECJAJDAgUCJALmAQUCqAHHAYkBqAFqAYkBLAFLAQ0BLAHuAA0B
        sADPAJEAsAByAJEANABTABUANAAVABUAwALAAqECwAKCAqECYwKCAkQCYwIlAkQCBgIlAucBBgLIAecBqQHIAYoBqQFrAYoB
        TAFrAS0BTAEOAS0B7wAOAdAA7wCxANAAkgCxAHMAkgBUAHMANQBUABYANQAWABYAwQLgAqICwQKDAqICRQJkAiYCRQIHAiYC
        yQHoAaoByQGLAaoBTQFsAS4BTQEPAS4B0QDwALIA0QCTALIAVQB0ADYAVQAXADYAwgLhAqMCwgJGAmUCJwJGAsoB6QGrAcoB
        TgFtAS8BTgHSAPEAswDSAFYAdQA3AFYAwwLiAkcCZgLLAeoBTwFuAdMA8gBXAHYA4ALgAmQCgwLoAQcCbAGLAfAADwF0AJMA
        FwAXAAADAAPhAgADhAKjAmUChAIIAicC6QEIAowBqwFtAYwBEAEvAfEAEAGUALMAdQCUABgANwAYABgAIAMgAwEDIAPiAgED
        pALDAoUCpAJmAoUCKAJHAgkCKALqAQkCrAHLAY0BrAFuAY0BMAFPAREBMAHyABEBtADTAJUAtAB2AJUAOABXABkAOAAZABkA
        QANAAyEDQAMCAyED4wICA8QC4wKlAsQChgKlAmcChgJIAmcCKQJIAgoCKQLrAQoCzAHrAa0BzAGOAa0BbwGOAVABbwExAVAB
        EgExAfMAEgHUAPMAtQDUAJYAtQB3AJYAWAB3ADkAWAAaADkAGgAaAEEDYAMiA0EDAwMiA8UC5AKmAsUChwKmAkkCaAIqAkkC
        CwIqAs0B7AGuAc0BjwGuAVEBcAEyAVEBEwEyAdUA9AC2ANUAlwC2AFkAeAA6AFkAGwA6AEIDYQMjA0IDxgLlAqcCxgJKAmkC
        KwJKAs4B7QGvAc4BUgFxATMBUgHWAPUAtwDWAFoAeQA7AFoAQwNiA8cC5gJLAmoCzwHuAVMBcgHXAPYAWwB6AGADYAPkAgMD
        aAKHAuwBCwJwAY8B9AATAXgAlwAbABsAgAOAA2EDgAMEAyMD5QIEA4gCpwJpAogCDAIrAu0BDAKQAa8BcQGQARQBMwH1ABQB
        mAC3AHkAmAAcADsAHAAcAKADoAOBA6ADYgOBAyQDQwMFAyQD5gIFA6gCxwKJAqgCagKJAiwCSwINAiwC7gENArABzwGRAbAB
        cgGRATQBUwEVATQB9gAVAbgA1wCZALgAegCZADwAWwAdADwAHQAdAMADwAOhA8ADggOhA2MDggNEA2MDJQNEAwYDJQPnAgYD
        yALnAqkCyAKKAqkCawKKAkwCawItAkwCDgItAu8BDgLQAe8BsQHQAZIBsQFzAZIBVAFzATUBVAEWATUB9wAWAdgA9wC5ANgA
        mgC5AHsAmgBcAHsAPQBcAB4APQAeAB4AwQPgA6IDwQODA6IDRQNkAyYDRQMHAyYDyQLoAqoCyQKLAqoCTQJsAi4CTQIPAi4C
        0QHwAbIB0QGTAbIBVQF0ATYBVQEXATYB2QD4ALoA2QCbALoAXQB8AD4AXQAfAD4AwgPhA6MDwgNGA2UDJwNGA8oC6QKrAsoC
        TgJtAi8CTgLSAfEBswHSAVYBdQE3AVYB2gD5ALsA2gBeAH0APwBeAMMD4gNHA2YDywLqAk8CbgLTAfIBVwF2AdsA+gBfAH4A
        ZAODA+gCBwNsAosC8AEPAnQBkwH4ABcBfACbAIQDowNlA4QDCAMnA+kCCAOMAqsCbQKMAhACLwLxARAClAGzAXUBlAEYATcB
        +QAYAZwAuwB9AJwApAPDA4UDpANmA4UDKANHAwkDKAPqAgkDrALLAo0CrAJuAo0CMAJPAhECMALyARECtAHTAZUBtAF2AZUB
        OAFXARkBOAH6ABkBvADbAJ0AvAB+AJ0AxAPjA6UDxAOGA6UDZwOGA0gDZwMpA0gDCgMpA+sCCgPMAusCrQLMAo4CrQJvAo4C
        UAJvAjECUAISAjEC8wESAtQB8wG1AdQBlgG1AXcBlgFYAXcBOQFYARoBOQH7ABoB3AD7AL0A3ACeAL0AfwCeAMUD5AOmA8UD
        hwOmA0kDaAMqA0kDCwMqA80C7AKuAs0CjwKuAlECcAIyAlECEwIyAtUB9AG2AdUBlwG2AVkBeAE6AVkBGwE6Ad0A/AC+AN0A
        nwC+AMYD5QOnA8YDSgNpAysDSgPOAu0CrwLOAlICcQIzAlIC1gH1AbcB1gFaAXkBOwFaAd4A/QC/AN4AxwPmA0sDagPPAu4C
        UwJyAtcB9gFbAXoB3wD+AGgDhwPsAgsDcAKPAvQBEwJ4AZcB/AAbAYgDpwNpA4gDDAMrA+0CDAOQAq8CcQKQAhQCMwL1ARQC
        mAG3AXkBmAEcATsB/QAcAagDxwOJA6gDagOJAywDSwMNAywD7gINA7ACzwKRArACcgKRAjQCUwIVAjQC9gEVArgB1wGZAbgB
        egGZATwBWwEdATwB/gAdAcgD5wOpA8gDigOpA2sDigNMA2sDLQNMAw4DLQPvAg4D0ALvArEC0AKSArECcwKSAlQCcwI1AlQC
        FgI1AvcBFgLYAfcBuQHYAZoBuQF7AZoBXAF7AT0BXAEeAT0B/wAeAckD6AOqA8kDiwOqA00DbAMuA00DDwMuA9EC8AKyAtEC
        kwKyAlUCdAI2AlUCFwI2AtkB+AG6AdkBmwG6AV0BfAE+AV0BHwE+AcoD6QOrA8oDTgNtAy8DTgPSAvECswLSAlYCdQI3AlYC
        2gH5AbsB2gFeAX0BPwFeAcsD6gNPA24D0wLyAlcCdgLbAfoBXwF+AWwDiwPwAg8DdAKTAvgBFwJ8AZsBjAOrA20DjAMQAy8D
        8QIQA5QCswJ1ApQCGAI3AvkBGAKcAbsBfQGcAawDywONA6wDbgONAzADTwMRAzAD8gIRA7QC0wKVArQCdgKVAjgCVwIZAjgC
        +gEZArwB2wGdAbwBfgGdAcwD6wOtA8wDjgOtA28DjgNQA28DMQNQAxIDMQPzAhID1ALzArUC1AKWArUCdwKWAlgCdwI5AlgC
        GgI5AvsBGgLcAfsBvQHcAZ4BvQF/AZ4BzQPsA64DzQOPA64DUQNwAzIDUQMTAzID1QL0ArYC1QKXArYCWQJ4AjoCWQIbAjoC
        3QH8Ab4B3QGfAb4BzgPtA68DzgNSA3EDMwNSA9YC9QK3AtYCWgJ5AjsCWgLeAf0BvwHeAc8D7gNTA3ID1wL2AlsCegLfAf4B
        cAOPA/QCEwN4ApcC/AEbApADrwNxA5ADFAMzA/UCFAOYArcCeQKYAhwCOwL9ARwCsAPPA5EDsANyA5EDNANTAxUDNAP2AhUD
        uALXApkCuAJ6ApkCPAJbAh0CPAL+AR0C0APvA7ED0AOSA7EDcwOSA1QDcwM1A1QDFgM1A/cCFgPYAvcCuQLYApoCuQJ7ApoC
        XAJ7Aj0CXAIeAj0C/wEeAtED8AOyA9EDkwOyA1UDdAM2A1UDFwM2A9kC+AK6AtkCmwK6Al0CfAI+Al0CHwI+AtID8QOzA9ID
        VgN1AzcDVgPaAvkCuwLaAl4CfQI/Al4C0wPyA1cDdgPbAvoCXwJ+AnQDkwP4AhcDfAKbApQDswN1A5QDGAM3A/kCGAOcArsC
        fQKcArQD0wOVA7QDdgOVAzgDVwMZAzgD+gIZA7wC2wKdArwCfgKdAtQD8wO1A9QDlgO1A3cDlgNYA3cDOQNYAxoDOQP7AhoD
        3AL7Ar0C3AKeAr0CfwKeAtUD9AO2A9UDlwO2A1kDeAM6A1kDGwM6A90C/AK+At0CnwK+AtYD9QO3A9YDWgN5AzsDWgPeAv0C
        vwLeAtcD9gNbA3oD3wL+AngDlwP8AhsDmAO3A3kDmAMcAzsD/QIcA7gD1wOZA7gDegOZAzwDWwMdAzwD/gIdA9gD9wO5A9gD
        mgO5A3sDmgNcA3sDPQNcAx4DPQP/Ah4D2QP4A7oD2QObA7oDXQN8Az4DXQMfAz4D2gP5A7sD2gNeA30DPwNeA9sD+gNfA34D
        fAObA5wDuwN9A5wDvAPbA50DvAN+A50D3AP7A70D3AOeA70DfwOeA90D/AO+A90DnwO+A94D/QO/A94D3wP+AwAAAAA=
        """,
        2050);

    public static ReadOnlySpan<short> GetDefaultScan(Vp9TransformSize transformSize)
    {
        return GetScan(transformSize, Vp9TransformType.DctDct);
    }

    public static ReadOnlySpan<short> GetScan(Vp9TransformSize transformSize, Vp9TransformType transformType)
    {
        return transformSize switch
        {
            Vp9TransformSize.Tx4X4 => transformType switch
            {
                Vp9TransformType.DctDct or Vp9TransformType.AdstAdst => DefaultScan4X4,
                Vp9TransformType.AdstDct => RowScan4X4,
                Vp9TransformType.DctAdst => ColScan4X4,
                _ => throw new ArgumentOutOfRangeException(nameof(transformType), transformType, "Unsupported VP9 transform type.")
            },
            Vp9TransformSize.Tx8X8 => transformType switch
            {
                Vp9TransformType.DctDct or Vp9TransformType.AdstAdst => DefaultScan8X8,
                Vp9TransformType.AdstDct => RowScan8X8,
                Vp9TransformType.DctAdst => ColScan8X8,
                _ => throw new ArgumentOutOfRangeException(nameof(transformType), transformType, "Unsupported VP9 transform type.")
            },
            Vp9TransformSize.Tx16X16 => transformType switch
            {
                Vp9TransformType.DctDct or Vp9TransformType.AdstAdst => DefaultScan16X16,
                Vp9TransformType.AdstDct => RowScan16X16,
                Vp9TransformType.DctAdst => ColScan16X16,
                _ => throw new ArgumentOutOfRangeException(nameof(transformType), transformType, "Unsupported VP9 transform type.")
            },
            Vp9TransformSize.Tx32X32 => DefaultScan32X32,
            _ => throw new NotSupportedException($"VP9 coefficient scan table for {transformSize} is not implemented yet.")
        };
    }

    public static ReadOnlySpan<short> GetDefaultNeighbors(Vp9TransformSize transformSize)
    {
        return GetNeighbors(transformSize, Vp9TransformType.DctDct);
    }

    public static ReadOnlySpan<short> GetNeighbors(Vp9TransformSize transformSize, Vp9TransformType transformType)
    {
        return transformSize switch
        {
            Vp9TransformSize.Tx4X4 => transformType switch
            {
                Vp9TransformType.DctDct or Vp9TransformType.AdstAdst => DefaultScan4X4Neighbors,
                Vp9TransformType.AdstDct => RowScan4X4Neighbors,
                Vp9TransformType.DctAdst => ColScan4X4Neighbors,
                _ => throw new ArgumentOutOfRangeException(nameof(transformType), transformType, "Unsupported VP9 transform type.")
            },
            Vp9TransformSize.Tx8X8 => transformType switch
            {
                Vp9TransformType.DctDct or Vp9TransformType.AdstAdst => DefaultScan8X8Neighbors,
                Vp9TransformType.AdstDct => RowScan8X8Neighbors,
                Vp9TransformType.DctAdst => ColScan8X8Neighbors,
                _ => throw new ArgumentOutOfRangeException(nameof(transformType), transformType, "Unsupported VP9 transform type.")
            },
            Vp9TransformSize.Tx16X16 => transformType switch
            {
                Vp9TransformType.DctDct or Vp9TransformType.AdstAdst => DefaultScan16X16Neighbors,
                Vp9TransformType.AdstDct => RowScan16X16Neighbors,
                Vp9TransformType.DctAdst => ColScan16X16Neighbors,
                _ => throw new ArgumentOutOfRangeException(nameof(transformType), transformType, "Unsupported VP9 transform type.")
            },
            Vp9TransformSize.Tx32X32 => DefaultScan32X32Neighbors,
            _ => throw new NotSupportedException($"VP9 coefficient neighbor table for {transformSize} is not implemented yet.")
        };
    }

    public static int GetBand(Vp9TransformSize transformSize, int coefficientIndex)
    {
        if (coefficientIndex < 0 || coefficientIndex >= GetMaximumEob(transformSize))
        {
            throw new ArgumentOutOfRangeException(nameof(coefficientIndex), "VP9 coefficient index is outside the transform block.");
        }

        if (transformSize == Vp9TransformSize.Tx4X4)
        {
            return coefficientIndex switch
            {
                0 => 0,
                <= 2 => 1,
                <= 5 => 2,
                <= 9 => 3,
                <= 12 => 4,
                _ => 5
            };
        }

        return coefficientIndex switch
        {
            0 => 0,
            <= 2 => 1,
            <= 5 => 2,
            <= 9 => 3,
            <= 20 => 4,
            _ => 5
        };
    }

    public static int GetMaximumEob(Vp9TransformSize transformSize)
    {
        return transformSize switch
        {
            Vp9TransformSize.Tx4X4 => 16,
            Vp9TransformSize.Tx8X8 => 64,
            Vp9TransformSize.Tx16X16 => 256,
            Vp9TransformSize.Tx32X32 => 1024,
            _ => throw new ArgumentOutOfRangeException(nameof(transformSize))
        };
    }

    public static int GetCoefficientContext(ReadOnlySpan<short> neighbors, ReadOnlySpan<byte> tokenCache, int coefficientIndex)
    {
        var neighborOffset = coefficientIndex * 2;
        return (1 + tokenCache[neighbors[neighborOffset]] + tokenCache[neighbors[neighborOffset + 1]]) >> 1;
    }

    private static short[] DecodeInt16Table(string base64, int expectedLength)
    {
        var bytes = Convert.FromBase64String(base64);
        if (bytes.Length != expectedLength * 2)
        {
            throw new InvalidOperationException("VP9 generated scan table has an unexpected length.");
        }

        var values = new short[expectedLength];
        for (var i = 0; i < values.Length; i++)
        {
            values[i] = BitConverter.ToInt16(bytes, i * 2);
        }

        return values;
    }
}
