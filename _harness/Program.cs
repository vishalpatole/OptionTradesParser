using OptionTradesParser;

var parser = new MessageParser();

var samples = new (string Label, string Text)[]
{
("OPEN-1 SWIFT SPY 757P", @"[SWIFT TRADES · LIVE DESK](https://swiftoptions.pro/desk)
![🟢](https://discord.com/assets/2d6d478121939bde.svg) BUY — SPY 757P · 0DTE
Entered **SPY Sep15 '26 757 Put**
**![⚠️](https://discord.com/assets/fb6fd920c79bd504.svg) Super Lotto Trade — Super RISKY**
[Open Live Dashboard →](https://swiftoptions.pro/desk)
Entry
`$0.455`

Contracts
`25`

Cost
`$1,138`

Trim Targets

```
25%   $0.569
50%   $0.683
75%   $0.796
100%  $0.910
```
"),
("OPEN-2 SWIFT SPX 7620P", @"BUY — SPX 7620P · 0DTE
Entered **SPX Sep14 '26 7620 Put**
**Super Lotto Trade — Super RISKY**
[Open Live Dashboard →](https://swiftoptions.pro/desk)
Entry
`$0.175`

Contracts
`25`

Cost
`$438`

Trim Targets

```
25%   $0.219
50%   $0.263
```
"),
("OPEN-3 NAMROOD SPCX", @"Buy To Open
1/3 position

```
SPCX 148C 9/18/2026 $2.88
```

[@Namrood](https://www.prismagroup.online/) - [LIVE DASHBOARD](https://dashboard.prismagroup.online/)"),
("OPEN-4 NAMROOD MSFT", @"Lotto Trade — RISKY

```
MSFT 502.5C 0DTE 0.6
```

![⚠️](https://discord.com/assets/fb6fd920c79bd504.svg) Size for what you can afford to lose --- 1% of your account balance.

[@Namrood](https://www.prismagroup.online/) - [LIVE DASHBOARD](https://dashboard.prismagroup.online/)"),
("OPEN-5 NAMROOD INTC", @"Lotto Trade — RISKY

```
INTC 97C 0DTE 0.9
```

Manage your risk!

[@Namrood](https://www.prismagroup.online/) - [LIVE DASHBOARD](https://dashboard.prismagroup.online/)"),
("OPEN-6 CHAMPSP SPY", @"SPY 759P  -1.6 @Shyamal"),
("OPEN-7 DEMON QQQ", @"BTO $QQQ 713c 09/16 @1.00

I will hold this trade through FOMC

@Demon"),
("OPEN-8 WAXUI SPY", @"@Waxui  **LOTTO**
SPY here
09/16 760C
Avg. 2.15
**STAY LIGHT**"),
("OPEN-9 SWIFT IWM", "$IWM 288 CALL @.32 \n@Swift"),
("UPDATE-6 NAMROOD NVDA", @"Trade Update - Manage your risk
FLIPPED - DIPS ARE BOUGHT

```
NVDA 207.5P 2026-09-14
0.8300  →  0.35   P/L: -57.83% ($-48.00)
```

[@Namrood](https://www.prismagroup.online/) - [LIVE DASHBOARD](https://dashboard.prismagroup.online/)"),
("CLOSE-1 SWIFT SPX 7700C", @"[SWIFT TRADES · LIVE DESK](https://swiftoptions.pro/desk)
![🚀](https://discord.com/assets/2a419df364f6817c.svg) +10% — SPX 7700C · Sep 21
**$11.15 → $12.30** · +10.3% (+$115.00/contract)
**Close or trim & set SL to breakeven.**
[Open Live Dashboard →](https://swiftoptions.pro/desk)

**Notes:**
![⚠️] Lotto Trade — RISKY"),
("CLOSE-2 SWIFT XSP 770C", @"[SWIFT TRADES · LIVE DESK](https://swiftoptions.pro/desk)
![🚀](https://discord.com/assets/2a419df364f6817c.svg) +10% — XSP 770C · Sep 21
**$1.125 → $1.25** · +11.1% (+$12.50/contract)
**Close or trim & set SL to breakeven.**
[Open Live Dashboard →](https://swiftoptions.pro/desk)"),
("CLOSE-3 SWIFT TRIM SPY 758P", @"TRIM +75% — SPY 758P · 0DTE
Sold **12 of 20** · avg **$0.938** · **8** still running

```
4 × $0.781   +25%
4 × $0.938   +50%
4 × $1.094   +75%
```

[Open Live Dashboard →](https://swiftoptions.pro/desk)
Entry
`$0.625`

Exit
`$0.938`

Locked In
`+$375.04`"),
("CLOSE-4 SWIFT SPY 757P", @"[SWIFT TRADES · LIVE DESK](https://swiftoptions.pro/desk)
![🚀] +10% — SPY 757P · 0DTE
**$0.455 → $0.505** · +11.0% (+$5.00/contract)
**Close or trim & set SL to breakeven.**
[Open Live Dashboard →](https://swiftoptions.pro/desk)

**Notes:**
**Super Lotto Trade — Super RISKY**"),
("CLOSE-5 NAMROOD INTC", @"Close or Trim & Set SL to BE
LATE ALERT - DISNT PUPLISH - BUT We get our money back - even if you got in late

```
INTC 97C 2026-09-14
0.9000  →  1.33   P/L: +47.78% ($43.00)
```

[@Namrood](https://www.prismagroup.online/) - [LIVE DASHBOARD](https://dashboard.prismagroup.online/)"),
("CLOSE-6 NAMROOD idea", @"Idea
0.68->1.10 INTC - ALL SHOULD BE IN PROFIT
[@Namrood](https://discord.gg/m9WAsJdFrn) - [Live trades dashboard](https://dashboard.prismagroup.online/)"),
("CLOSE-7 NAMROOD MSFT", @"Close or Trim & Set SL to BE

```
MSFT 502.5C 2026-09-14
0.6000  →  0.9   P/L: +50.00% ($30.00)
```

[@Namrood](https://www.prismagroup.online/) - [LIVE DASHBOARD](https://dashboard.prismagroup.online/)"),
};

Console.WriteLine($"{"SAMPLE",-28} {"TRADER",-13} {"ACTION",-12} {"CONTRACT",-22} {"PRICE",-8} {"FRAC",-6} {"AMBIG",-6} RISK");
Console.WriteLine(new string('-', 120));
foreach (var (label, text) in samples)
{
    var r = parser.TryParse(text);
    if (r == null) { Console.WriteLine($"{label,-28} SKIPPED"); continue; }
    string contract = $"{r.Ticker} {r.Expiration} {r.Strike}{(r.OptionType == "CALL" ? "C" : "P")}";
    Console.WriteLine($"{label,-28} {r.TraderName,-13} {r.ActionType,-12} {contract,-22} {r.PricePaid,-8} {r.TrimFraction,-6:F2} {r.SizingAmbiguous,-6} {r.RiskCategory}");
}

var waxuiContext = new TradeContractContext("SPY", "CALL", 760, "20260916");
var contextualTrim = parser.TryParse(
    "@Waxui  Trim SPY here\n2.15 - 2.60 ![✅](https://discord.com/assets/43b7ead1fb91b731.svg) 21%",
    (trader, ticker) => trader == "WAXUI" && ticker == "SPY" ? waxuiContext : null);
if (contextualTrim != null)
{
    string contract = $"{contextualTrim.Ticker} {contextualTrim.Expiration} {contextualTrim.Strike}{(contextualTrim.OptionType == "CALL" ? "C" : "P")}";
    Console.WriteLine($"{"TRIM-8 WAXUI SPY",-28} {contextualTrim.TraderName,-13} {contextualTrim.ActionType,-12} {contract,-22} {contextualTrim.PricePaid,-8} {contextualTrim.TrimFraction,-6:F2} {contextualTrim.SizingAmbiguous,-6} {contextualTrim.RiskCategory}");
}
