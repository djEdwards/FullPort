namespace FullPort.Models;

/// <summary>
/// Predefined risk-based allocation profiles - ordered from lowest to highest risk
/// </summary>
public enum RiskProfile
{
    UltraConservative,  // Capital preservation - mostly bonds/cash, minimal equity
    Conservative,       // Low risk - heavy bonds, some equity
    Income,             // Income-focused - dividends, bonds, REITs
    Balanced,           // Classic 60/40 all-weather approach
    Moderate,           // Moderate growth with diversification
    DividendGrowth,     // Focus on dividend growers and quality stocks
    Growth,             // Growth-focused - higher equity, some alternatives
    Aggressive,         // High equity - growth stocks, international, crypto
    UltraAggressive,    // Maximum growth - heavy crypto, small caps, emerging
    Custom              // User-defined allocations
}

/// <summary>
/// Asset classes for categorization
/// </summary>
public enum AssetClass
{
    USStock,
    InternationalStock,
    Bond,
    Cash,
    Commodity,
    RealEstate,
    Crypto,
    Other
}

/// <summary>
/// Represents a target allocation for a specific symbol or asset class
/// </summary>
public class TargetAllocation
{
    public string? Symbol { get; set; }
    public AssetClass? AssetClass { get; set; }
    public decimal TargetPercent { get; set; }
    public decimal CurrentPercent { get; set; }
    public decimal CurrentValue { get; set; }
    public decimal TargetValue { get; set; }
    public decimal DifferenceValue { get; set; }
    public decimal DifferencePercent { get; set; }
}

/// <summary>
/// A rebalancing model/strategy
/// </summary>
public class RebalanceModel
{
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public RiskProfile RiskProfile { get; set; }
    public List<TargetAllocation> Allocations { get; set; } = [];
    public decimal CashReservePercent { get; set; } = 5; // Keep at least 5% in cash
    public bool RebalanceByAssetClass { get; set; } = false; // true = by asset class, false = by symbol
}

/// <summary>
/// A trade order to be executed for rebalancing
/// </summary>
public class RebalanceTrade
{
    public string Symbol { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string Action { get; set; } = string.Empty; // BUY or SELL
    public decimal Quantity { get; set; }
    public decimal EstimatedPrice { get; set; }
    public decimal EstimatedValue { get; set; }
    public string OrderType { get; set; } = "MARKET"; // MARKET, LIMIT
    public decimal? LimitPrice { get; set; }
    public string AccountIdKey { get; set; } = string.Empty;
    public string? PreviewId { get; set; } // From E*TRADE preview response
}

/// <summary>
/// Request to calculate rebalancing trades
/// </summary>
public class RebalanceRequest
{
    public string? AccountIdKey { get; set; } // null = all accounts
    public RebalanceModel Model { get; set; } = new();
    public bool SellToRebalance { get; set; } = true; // Allow selling overweight positions
    public decimal MinimumTradeValue { get; set; } = 50; // Minimum trade size
    public decimal TolerancePercent { get; set; } = 2; // Don't trade if within tolerance
}

/// <summary>
/// Response with calculated rebalancing trades
/// </summary>
public class RebalanceResponse
{
    public decimal TotalPortfolioValue { get; set; }
    public List<TargetAllocation> CurrentAllocations { get; set; } = [];
    public List<TargetAllocation> TargetAllocations { get; set; } = [];
    public List<RebalanceTrade> RequiredTrades { get; set; } = [];
    public decimal EstimatedTotalBuys { get; set; }
    public decimal EstimatedTotalSells { get; set; }
    public int TradeCount { get; set; }
    public List<string> Warnings { get; set; } = [];
}

/// <summary>
/// Request to execute rebalancing trades
/// </summary>
public class ExecuteRebalanceRequest
{
    public List<RebalanceTrade> Trades { get; set; } = [];
    public bool PreviewOnly { get; set; } = true; // Default to preview
}

/// <summary>
/// Result of trade execution
/// </summary>
public class TradeExecutionResult
{
    public string Symbol { get; set; } = string.Empty;
    public string Action { get; set; } = string.Empty;
    public decimal Quantity { get; set; }
    public bool Success { get; set; }
    public string? OrderId { get; set; }
    public string? PreviewId { get; set; }
    public string? ErrorMessage { get; set; }
    public decimal? FilledPrice { get; set; }
    public string Status { get; set; } = string.Empty; // PREVIEW, PLACED, FILLED, FAILED
}

/// <summary>
/// Common ETF mappings for asset classes - expanded to cover all suggested ETFs
/// </summary>
public static class AssetClassMappings
{
    public static readonly Dictionary<string, AssetClass> CommonETFs = new()
    {
        // US Stocks - Total Market / Broad
        { "VTI", AssetClass.USStock },
        { "ITOT", AssetClass.USStock },
        { "SPTM", AssetClass.USStock },

        // US Stocks - Large Cap
        { "VOO", AssetClass.USStock },
        { "SPY", AssetClass.USStock },
        { "IVV", AssetClass.USStock },
        { "SCHX", AssetClass.USStock },

        // US Stocks - Growth
        { "VUG", AssetClass.USStock },
        { "QQQ", AssetClass.USStock },
        { "QQQM", AssetClass.USStock },
        { "VGT", AssetClass.USStock },
        { "MGK", AssetClass.USStock },
        { "SCHG", AssetClass.USStock },

        // US Stocks - Value
        { "VTV", AssetClass.USStock },
        { "SCHV", AssetClass.USStock },
        { "RPV", AssetClass.USStock },

        // US Stocks - Dividend
        { "SCHD", AssetClass.USStock },
        { "VYM", AssetClass.USStock },
        { "DVY", AssetClass.USStock },
        { "DGRO", AssetClass.USStock },
        { "NOBL", AssetClass.USStock },
        { "VIG", AssetClass.USStock },

        // US Stocks - Small/Mid Cap
        { "VB", AssetClass.USStock },
        { "IJR", AssetClass.USStock },
        { "VXF", AssetClass.USStock },
        { "VO", AssetClass.USStock },
        { "IJH", AssetClass.USStock },
        { "IWM", AssetClass.USStock },
        { "SCHA", AssetClass.USStock },

        // US Stocks - Factor
        { "MTUM", AssetClass.USStock },
        { "QUAL", AssetClass.USStock },
        { "USMV", AssetClass.USStock },
        { "VLUE", AssetClass.USStock },
        { "RSP", AssetClass.USStock },

        // International Stocks - Total/Broad
        { "VXUS", AssetClass.InternationalStock },
        { "IXUS", AssetClass.InternationalStock },

        // International Stocks - Developed
        { "VEA", AssetClass.InternationalStock },
        { "IEFA", AssetClass.InternationalStock },
        { "EFA", AssetClass.InternationalStock },
        { "SCHF", AssetClass.InternationalStock },
        { "SPDW", AssetClass.InternationalStock },

        // International Stocks - Emerging
        { "VWO", AssetClass.InternationalStock },
        { "IEMG", AssetClass.InternationalStock },
        { "EEM", AssetClass.InternationalStock },
        { "SCHE", AssetClass.InternationalStock },

        // International Stocks - Regional
        { "VGK", AssetClass.InternationalStock },
        { "EWJ", AssetClass.InternationalStock },
        { "VPL", AssetClass.InternationalStock },
        { "MCHI", AssetClass.InternationalStock },
        { "EWZ", AssetClass.InternationalStock },
        { "INDA", AssetClass.InternationalStock },
        { "EWG", AssetClass.InternationalStock },
        { "EWU", AssetClass.InternationalStock },
        { "EWC", AssetClass.InternationalStock },
        { "VYMI", AssetClass.InternationalStock },
        { "IDV", AssetClass.InternationalStock },
        { "VSS", AssetClass.InternationalStock },
        { "SCZ", AssetClass.InternationalStock },

        // Bonds - Aggregate
        { "BND", AssetClass.Bond },
        { "AGG", AssetClass.Bond },
        { "SCHZ", AssetClass.Bond },
        { "SPAB", AssetClass.Bond },

        // Bonds - Treasury
        { "GOVT", AssetClass.Bond },
        { "SHY", AssetClass.Bond },
        { "IEI", AssetClass.Bond },
        { "IEF", AssetClass.Bond },
        { "TLT", AssetClass.Bond },
        { "VGSH", AssetClass.Bond },
        { "VGIT", AssetClass.Bond },
        { "VGLT", AssetClass.Bond },
        { "EDV", AssetClass.Bond },

        // Bonds - TIPS
        { "TIP", AssetClass.Bond },
        { "VTIP", AssetClass.Bond },
        { "SCHP", AssetClass.Bond },

        // Bonds - Corporate
        { "VCIT", AssetClass.Bond },
        { "LQD", AssetClass.Bond },
        { "VCSH", AssetClass.Bond },
        { "VCLT", AssetClass.Bond },
        { "IGIB", AssetClass.Bond },

        // Bonds - High Yield
        { "HYG", AssetClass.Bond },
        { "JNK", AssetClass.Bond },
        { "SHYG", AssetClass.Bond },
        { "USHY", AssetClass.Bond },

        // Bonds - Municipal
        { "MUB", AssetClass.Bond },
        { "VTEB", AssetClass.Bond },
        { "TFI", AssetClass.Bond },
        { "SUB", AssetClass.Bond },
        { "HYD", AssetClass.Bond },

        // Bonds - International
        { "BNDX", AssetClass.Bond },
        { "IAGG", AssetClass.Bond },
        { "EMB", AssetClass.Bond },
        { "VWOB", AssetClass.Bond },

        // Bonds - Floating/Short
        { "FLOT", AssetClass.Bond },
        { "BKLN", AssetClass.Bond },
        { "BIL", AssetClass.Bond },
        { "SGOV", AssetClass.Bond },
        { "USFR", AssetClass.Bond },

        // Commodities - Gold
        { "GLD", AssetClass.Commodity },
        { "IAU", AssetClass.Commodity },
        { "GLDM", AssetClass.Commodity },
        { "SGOL", AssetClass.Commodity },

        // Commodities - Silver & Precious Metals
        { "SLV", AssetClass.Commodity },
        { "SIVR", AssetClass.Commodity },
        { "GLTR", AssetClass.Commodity },
        { "PPLT", AssetClass.Commodity },

        // Commodities - Broad
        { "DJP", AssetClass.Commodity },
        { "GSG", AssetClass.Commodity },
        { "DBC", AssetClass.Commodity },
        { "PDBC", AssetClass.Commodity },
        { "COM", AssetClass.Commodity },

        // Commodities - Energy
        { "USO", AssetClass.Commodity },
        { "UNG", AssetClass.Commodity },
        { "XLE", AssetClass.Commodity },

        // Commodities - Agriculture
        { "DBA", AssetClass.Commodity },
        { "WEAT", AssetClass.Commodity },
        { "CORN", AssetClass.Commodity },

        // Real Estate - Broad
        { "VNQ", AssetClass.RealEstate },
        { "SCHH", AssetClass.RealEstate },
        { "IYR", AssetClass.RealEstate },
        { "XLRE", AssetClass.RealEstate },
        { "USRT", AssetClass.RealEstate },

        // Real Estate - Specialized
        { "MORT", AssetClass.RealEstate },
        { "REM", AssetClass.RealEstate },
        { "INDS", AssetClass.RealEstate },

        // Real Estate - International
        { "VNQI", AssetClass.RealEstate },
        { "RWX", AssetClass.RealEstate },
        { "IFGL", AssetClass.RealEstate },
        { "REET", AssetClass.RealEstate },
        { "SRET", AssetClass.RealEstate },

        // Crypto
        { "IBIT", AssetClass.Crypto },
        { "FBTC", AssetClass.Crypto },
        { "ARKB", AssetClass.Crypto },
        { "BITB", AssetClass.Crypto },
        { "GBTC", AssetClass.Crypto },
        { "ETHA", AssetClass.Crypto },
        { "FETH", AssetClass.Crypto },
        { "ETHE", AssetClass.Crypto },
        { "BTOP", AssetClass.Crypto },
    };

    public static AssetClass GetAssetClass(string symbol)
    {
        if (CommonETFs.TryGetValue(symbol.ToUpper(), out var assetClass))
            return assetClass;

        // Default heuristics
        return AssetClass.USStock; // Default assumption
    }

    public static readonly Dictionary<RiskProfile, Dictionary<AssetClass, decimal>> RiskProfileAllocations = new()
    {
        // Ultra-Conservative: Capital preservation, retirees, emergency funds
        {
            RiskProfile.UltraConservative, new Dictionary<AssetClass, decimal>
            {
                { AssetClass.USStock, 5 },
                { AssetClass.InternationalStock, 5 },
                { AssetClass.Bond, 60 },
                { AssetClass.RealEstate, 5 },
                { AssetClass.Commodity, 5 },
                { AssetClass.Cash, 20 }
            }
        },
        // Conservative: Low risk, income focus with some growth
        {
            RiskProfile.Conservative, new Dictionary<AssetClass, decimal>
            {
                { AssetClass.USStock, 15 },
                { AssetClass.InternationalStock, 10 },
                { AssetClass.Bond, 50 },
                { AssetClass.RealEstate, 10 },
                { AssetClass.Commodity, 5 },
                { AssetClass.Cash, 10 }
            }
        },
        // Income: Maximize dividends and yield from all asset classes
        {
            RiskProfile.Income, new Dictionary<AssetClass, decimal>
            {
                { AssetClass.USStock, 25 },       // Dividend stocks (SCHD, VYM, DVY)
                { AssetClass.InternationalStock, 10 }, // Intl dividend (VYMI)
                { AssetClass.Bond, 35 },          // Corporate & high yield bonds
                { AssetClass.RealEstate, 20 },    // REITs for income
                { AssetClass.Commodity, 5 },      // Gold as hedge
                { AssetClass.Cash, 5 }
            }
        },
        // Balanced: Classic 60/40 all-weather approach
        {
            RiskProfile.Balanced, new Dictionary<AssetClass, decimal>
            {
                { AssetClass.USStock, 40 },
                { AssetClass.InternationalStock, 20 },
                { AssetClass.Bond, 30 },
                { AssetClass.RealEstate, 5 },
                { AssetClass.Commodity, 5 }
            }
        },
        // Moderate: Balanced growth with alternatives
        {
            RiskProfile.Moderate, new Dictionary<AssetClass, decimal>
            {
                { AssetClass.USStock, 35 },
                { AssetClass.InternationalStock, 15 },
                { AssetClass.Bond, 25 },
                { AssetClass.RealEstate, 10 },
                { AssetClass.Commodity, 5 },
                { AssetClass.Crypto, 5 },
                { AssetClass.Cash, 5 }
            }
        },
        // Dividend Growth: Focus on dividend aristocrats and growers
        {
            RiskProfile.DividendGrowth, new Dictionary<AssetClass, decimal>
            {
                { AssetClass.USStock, 50 },       // NOBL, VIG, DGRO
                { AssetClass.InternationalStock, 15 }, // IDV, VYMI
                { AssetClass.Bond, 15 },
                { AssetClass.RealEstate, 15 },    // REITs for income
                { AssetClass.Cash, 5 }
            }
        },
        // Growth: Capital appreciation focus
        {
            RiskProfile.Growth, new Dictionary<AssetClass, decimal>
            {
                { AssetClass.USStock, 50 },       // VUG, QQQ, growth stocks
                { AssetClass.InternationalStock, 20 },
                { AssetClass.Bond, 10 },
                { AssetClass.RealEstate, 5 },
                { AssetClass.Commodity, 5 },
                { AssetClass.Crypto, 5 },
                { AssetClass.Cash, 5 }
            }
        },
        // Aggressive: High equity with alternatives
        {
            RiskProfile.Aggressive, new Dictionary<AssetClass, decimal>
            {
                { AssetClass.USStock, 45 },
                { AssetClass.InternationalStock, 20 },
                { AssetClass.Bond, 5 },
                { AssetClass.RealEstate, 10 },
                { AssetClass.Commodity, 5 },
                { AssetClass.Crypto, 10 },
                { AssetClass.Cash, 5 }
            }
        },
        // Ultra-Aggressive: Maximum growth, high speculation
        {
            RiskProfile.UltraAggressive, new Dictionary<AssetClass, decimal>
            {
                { AssetClass.USStock, 40 },       // Growth & small caps
                { AssetClass.InternationalStock, 20 }, // Emerging markets focus
                { AssetClass.RealEstate, 5 },
                { AssetClass.Commodity, 10 },     // Includes energy
                { AssetClass.Crypto, 25 }         // High crypto allocation
            }
        }
    };

    /// <summary>
    /// Suggested ETFs for building a portfolio by asset class
    /// Expanded selection covering various strategies, styles, and market segments
    /// </summary>
    public static readonly Dictionary<AssetClass, List<SuggestedETF>> SuggestedETFs = new()
    {
        {
            AssetClass.USStock, new List<SuggestedETF>
            {
                // Total Market / Broad Exposure
                new("VTI", "Vanguard Total Stock Market ETF", "Broad US market - all cap sizes"),
                new("ITOT", "iShares Core S&P Total US Stock Market ETF", "Total US market exposure"),
                new("SPTM", "SPDR Portfolio S&P 1500 Composite Stock Market ETF", "Large/mid/small cap blend"),

                // Large Cap
                new("VOO", "Vanguard S&P 500 ETF", "Large-cap US stocks (low cost)"),
                new("SPY", "SPDR S&P 500 ETF", "Most liquid S&P 500 ETF"),
                new("IVV", "iShares Core S&P 500 ETF", "S&P 500 (BlackRock)"),

                // Growth
                new("VUG", "Vanguard Growth ETF", "Large-cap growth stocks"),
                new("QQQ", "Invesco QQQ Trust", "Nasdaq 100 / Tech focused"),
                new("QQQM", "Invesco Nasdaq 100 ETF", "Nasdaq 100 (lower cost than QQQ)"),
                new("VGT", "Vanguard Information Technology ETF", "Tech sector focus"),
                new("MGK", "Vanguard Mega Cap Growth ETF", "Mega-cap growth"),
                new("SCHG", "Schwab US Large-Cap Growth ETF", "Large-cap growth (low cost)"),

                // Value
                new("VTV", "Vanguard Value ETF", "Large-cap value stocks"),
                new("SCHV", "Schwab US Large-Cap Value ETF", "Large-cap value (low cost)"),
                new("RPV", "Invesco S&P 500 Pure Value ETF", "Deep value strategy"),

                // Dividend / Income
                new("SCHD", "Schwab US Dividend Equity ETF", "Quality dividend stocks"),
                new("VYM", "Vanguard High Dividend Yield ETF", "High dividend yield"),
                new("DVY", "iShares Select Dividend ETF", "Dividend-weighted"),
                new("DGRO", "iShares Core Dividend Growth ETF", "Dividend growth focus"),
                new("NOBL", "ProShares S&P 500 Dividend Aristocrats ETF", "25+ years dividend growth"),
                new("VIG", "Vanguard Dividend Appreciation ETF", "Dividend growers"),

                // Small & Mid Cap
                new("VB", "Vanguard Small-Cap ETF", "US small-cap stocks"),
                new("IJR", "iShares Core S&P Small-Cap ETF", "S&P 600 small-cap"),
                new("VXF", "Vanguard Extended Market ETF", "Mid/small cap (ex-S&P 500)"),
                new("VO", "Vanguard Mid-Cap ETF", "US mid-cap stocks"),
                new("IJH", "iShares Core S&P Mid-Cap ETF", "S&P 400 mid-cap"),
                new("IWM", "iShares Russell 2000 ETF", "Small-cap Russell 2000"),
                new("SCHA", "Schwab US Small-Cap ETF", "Small-cap (low cost)"),

                // Factor / Smart Beta
                new("MTUM", "iShares MSCI USA Momentum Factor ETF", "Momentum factor"),
                new("QUAL", "iShares MSCI USA Quality Factor ETF", "Quality factor"),
                new("USMV", "iShares MSCI USA Min Vol Factor ETF", "Low volatility"),
                new("VLUE", "iShares MSCI USA Value Factor ETF", "Value factor"),

                // Equal Weight
                new("RSP", "Invesco S&P 500 Equal Weight ETF", "Equal-weighted S&P 500"),
            }
        },
        {
            AssetClass.InternationalStock, new List<SuggestedETF>
            {
                // Total International
                new("VXUS", "Vanguard Total International Stock ETF", "All international ex-US"),
                new("IXUS", "iShares Core MSCI Total International Stock ETF", "Broad international"),

                // Developed Markets
                new("VEA", "Vanguard FTSE Developed Markets ETF", "Developed markets ex-US"),
                new("IEFA", "iShares Core MSCI EAFE ETF", "Europe, Australasia, Far East"),
                new("EFA", "iShares MSCI EAFE ETF", "EAFE (most liquid)"),
                new("SCHF", "Schwab International Equity ETF", "Developed ex-US (low cost)"),
                new("SPDW", "SPDR Portfolio Developed World ex-US ETF", "Developed markets"),

                // Emerging Markets
                new("VWO", "Vanguard FTSE Emerging Markets ETF", "Broad emerging markets"),
                new("IEMG", "iShares Core MSCI Emerging Markets ETF", "Emerging markets"),
                new("EEM", "iShares MSCI Emerging Markets ETF", "EM (most liquid)"),
                new("SCHE", "Schwab Emerging Markets Equity ETF", "EM (low cost)"),

                // Regional
                new("VGK", "Vanguard FTSE Europe ETF", "European stocks"),
                new("EWJ", "iShares MSCI Japan ETF", "Japanese stocks"),
                new("VPL", "Vanguard FTSE Pacific ETF", "Asia Pacific developed"),
                new("MCHI", "iShares MSCI China ETF", "Chinese stocks"),
                new("EWZ", "iShares MSCI Brazil ETF", "Brazilian stocks"),
                new("INDA", "iShares MSCI India ETF", "Indian stocks"),
                new("EWG", "iShares MSCI Germany ETF", "German stocks"),
                new("EWU", "iShares MSCI United Kingdom ETF", "UK stocks"),
                new("EWC", "iShares MSCI Canada ETF", "Canadian stocks"),

                // International Dividend
                new("VYMI", "Vanguard International High Dividend Yield ETF", "International dividends"),
                new("IDV", "iShares International Select Dividend ETF", "International dividend"),

                // International Small Cap
                new("VSS", "Vanguard FTSE All-World ex-US Small-Cap ETF", "International small-cap"),
                new("SCZ", "iShares MSCI EAFE Small-Cap ETF", "Developed small-cap"),
            }
        },
        {
            AssetClass.Bond, new List<SuggestedETF>
            {
                // Total Bond Market
                new("BND", "Vanguard Total Bond Market ETF", "Broad US investment-grade bonds"),
                new("AGG", "iShares Core US Aggregate Bond ETF", "US aggregate bonds"),
                new("SCHZ", "Schwab US Aggregate Bond ETF", "US aggregate (low cost)"),
                new("SPAB", "SPDR Portfolio Aggregate Bond ETF", "US aggregate bonds"),

                // Treasury Bonds
                new("GOVT", "iShares US Treasury Bond ETF", "All-maturity treasuries"),
                new("SHY", "iShares 1-3 Year Treasury Bond ETF", "Short-term treasuries"),
                new("IEI", "iShares 3-7 Year Treasury Bond ETF", "Intermediate treasuries"),
                new("IEF", "iShares 7-10 Year Treasury Bond ETF", "Medium-term treasuries"),
                new("TLT", "iShares 20+ Year Treasury Bond ETF", "Long-term treasuries"),
                new("VGSH", "Vanguard Short-Term Treasury ETF", "Short treasuries"),
                new("VGIT", "Vanguard Intermediate-Term Treasury ETF", "Intermediate treasuries"),
                new("VGLT", "Vanguard Long-Term Treasury ETF", "Long treasuries"),
                new("EDV", "Vanguard Extended Duration Treasury ETF", "Extended duration"),

                // TIPS (Inflation Protected)
                new("TIP", "iShares TIPS Bond ETF", "Inflation-protected securities"),
                new("VTIP", "Vanguard Short-Term Inflation-Protected Securities ETF", "Short-term TIPS"),
                new("SCHP", "Schwab US TIPS ETF", "TIPS (low cost)"),

                // Corporate Bonds
                new("VCIT", "Vanguard Intermediate-Term Corporate Bond ETF", "Investment-grade corporate"),
                new("LQD", "iShares iBoxx $ Investment Grade Corporate Bond ETF", "IG corporate bonds"),
                new("VCSH", "Vanguard Short-Term Corporate Bond ETF", "Short-term corporate"),
                new("VCLT", "Vanguard Long-Term Corporate Bond ETF", "Long-term corporate"),
                new("IGIB", "iShares 5-10 Year Investment Grade Corporate Bond ETF", "Intermediate corporate"),

                // High Yield / Junk Bonds
                new("HYG", "iShares iBoxx $ High Yield Corporate Bond ETF", "High yield corporate"),
                new("JNK", "SPDR Bloomberg High Yield Bond ETF", "High yield (junk bonds)"),
                new("SHYG", "iShares 0-5 Year High Yield Corporate Bond ETF", "Short high yield"),
                new("USHY", "iShares Broad USD High Yield Corporate Bond ETF", "Broad high yield"),

                // Municipal Bonds
                new("MUB", "iShares National Muni Bond ETF", "Tax-free municipal bonds"),
                new("VTEB", "Vanguard Tax-Exempt Bond ETF", "Tax-exempt munis"),
                new("TFI", "SPDR Nuveen Bloomberg Municipal Bond ETF", "Municipal bonds"),
                new("SUB", "iShares Short-Term National Muni Bond ETF", "Short-term munis"),
                new("HYD", "VanEck High Yield Muni ETF", "High yield munis"),

                // International Bonds
                new("BNDX", "Vanguard Total International Bond ETF", "International bonds hedged"),
                new("IAGG", "iShares Core International Aggregate Bond ETF", "International aggregate"),
                new("EMB", "iShares JP Morgan USD Emerging Markets Bond ETF", "EM bonds USD"),
                new("VWOB", "Vanguard Emerging Markets Government Bond ETF", "EM government bonds"),

                // Floating Rate / Bank Loans
                new("FLOT", "iShares Floating Rate Bond ETF", "Floating rate notes"),
                new("BKLN", "Invesco Senior Loan ETF", "Senior bank loans"),

                // Ultra Short / Money Market Alternative
                new("BIL", "SPDR Bloomberg 1-3 Month T-Bill ETF", "T-Bills (near cash)"),
                new("SGOV", "iShares 0-3 Month Treasury Bond ETF", "Ultra-short treasuries"),
                new("USFR", "WisdomTree Floating Rate Treasury Fund", "Floating rate treasuries"),
            }
        },
        {
            AssetClass.Commodity, new List<SuggestedETF>
            {
                // Gold
                new("GLD", "SPDR Gold Shares", "Gold bullion (most liquid)"),
                new("IAU", "iShares Gold Trust", "Gold bullion (lower cost)"),
                new("GLDM", "SPDR Gold MiniShares Trust", "Gold (lowest cost)"),
                new("SGOL", "Aberdeen Standard Physical Gold Shares ETF", "Physical gold"),

                // Silver
                new("SLV", "iShares Silver Trust", "Silver bullion"),
                new("SIVR", "Aberdeen Standard Physical Silver Shares ETF", "Physical silver"),

                // Precious Metals
                new("GLTR", "Aberdeen Standard Physical Precious Metals Basket", "Gold, silver, platinum, palladium"),
                new("PPLT", "Aberdeen Standard Physical Platinum Shares ETF", "Platinum"),

                // Broad Commodities
                new("DJP", "iPath Bloomberg Commodity Index ETN", "Broad commodity exposure"),
                new("GSG", "iShares S&P GSCI Commodity-Indexed Trust", "Diversified commodities"),
                new("DBC", "Invesco DB Commodity Index Tracking Fund", "Diversified commodities"),
                new("PDBC", "Invesco Optimum Yield Diversified Commodity Strategy", "Commodity futures"),
                new("COM", "Direxion Auspice Broad Commodity Strategy ETF", "Managed futures"),

                // Energy
                new("USO", "United States Oil Fund LP", "Crude oil futures"),
                new("UNG", "United States Natural Gas Fund LP", "Natural gas futures"),
                new("XLE", "Energy Select Sector SPDR Fund", "Energy sector stocks"),

                // Agriculture
                new("DBA", "Invesco DB Agriculture Fund", "Agricultural commodities"),
                new("WEAT", "Teucrium Wheat Fund", "Wheat futures"),
                new("CORN", "Teucrium Corn Fund", "Corn futures"),

                // Bitcoin / Crypto (ETFs)
                new("IBIT", "iShares Bitcoin Trust ETF", "Spot Bitcoin"),
                new("FBTC", "Fidelity Wise Origin Bitcoin Fund", "Spot Bitcoin (Fidelity)"),
                new("GBTC", "Grayscale Bitcoin Trust ETF", "Bitcoin trust"),
                new("ETHE", "Grayscale Ethereum Trust ETF", "Ethereum trust"),
            }
        },
        {
            AssetClass.RealEstate, new List<SuggestedETF>
            {
                // Broad REITs
                new("VNQ", "Vanguard Real Estate ETF", "US REITs - broad exposure"),
                new("SCHH", "Schwab US REIT ETF", "US REITs (low cost)"),
                new("IYR", "iShares US Real Estate ETF", "US real estate"),
                new("XLRE", "Real Estate Select Sector SPDR Fund", "S&P 500 real estate"),
                new("USRT", "iShares Core US REIT ETF", "Core US REITs"),

                // Specialized REITs
                new("MORT", "VanEck Mortgage REIT Income ETF", "Mortgage REITs"),
                new("REM", "iShares Mortgage Real Estate ETF", "Mortgage REITs"),
                new("INDS", "Pacer Industrial Real Estate ETF", "Industrial/warehouse REITs"),

                // International REITs
                new("VNQI", "Vanguard Global ex-US Real Estate ETF", "International REITs"),
                new("RWX", "SPDR Dow Jones International Real Estate ETF", "International real estate"),
                new("IFGL", "iShares International Developed Real Estate ETF", "Developed market REITs"),

                // Real Estate Income
                new("REET", "iShares Global REIT ETF", "Global REITs"),
                new("SRET", "Global X SuperDividend REIT ETF", "High dividend REITs"),
            }
        },
        {
            AssetClass.Crypto, new List<SuggestedETF>
            {
                // Bitcoin
                new("IBIT", "iShares Bitcoin Trust ETF", "Spot Bitcoin (BlackRock)"),
                new("FBTC", "Fidelity Wise Origin Bitcoin Fund", "Spot Bitcoin (Fidelity)"),
                new("ARKB", "ARK 21Shares Bitcoin ETF", "Spot Bitcoin (ARK)"),
                new("BITB", "Bitwise Bitcoin ETF", "Spot Bitcoin (Bitwise)"),
                new("GBTC", "Grayscale Bitcoin Trust ETF", "Bitcoin trust (converted)"),

                // Ethereum
                new("ETHA", "iShares Ethereum Trust ETF", "Spot Ethereum (BlackRock)"),
                new("FETH", "Fidelity Ethereum Fund", "Spot Ethereum (Fidelity)"),
                new("ETHE", "Grayscale Ethereum Trust ETF", "Ethereum trust"),

                // Crypto Diversified
                new("BTOP", "Bitwise Bitcoin and Ether Equal Weight Strategy ETF", "BTC + ETH equal weight"),
            }
        }
    };
}

/// <summary>
/// A suggested ETF for portfolio building
/// </summary>
public class SuggestedETF
{
    public string Symbol { get; set; }
    public string Name { get; set; }
    public string Description { get; set; }
    public decimal? CurrentPrice { get; set; }
    public decimal? ChangePercent { get; set; }

    public SuggestedETF(string symbol, string name, string description)
    {
        Symbol = symbol;
        Name = name;
        Description = description;
    }
}

/// <summary>
/// Request to build a new portfolio
/// </summary>
public class BuildPortfolioRequest
{
    public string? AccountIdKey { get; set; }
    public decimal CashToInvest { get; set; }
    public int RiskProfile { get; set; } // 0=Conservative, 1=Moderate, 2=Aggressive
    public decimal CashReservePercent { get; set; } = 5; // Keep 5% as cash
    public List<PortfolioBuildItem> Items { get; set; } = [];
}


/// <summary>
/// An item in the portfolio to build
/// </summary>
public class PortfolioBuildItem
{
    public string Symbol { get; set; } = "";
    public string AssetClass { get; set; } = ""; // Use string instead of enum for flexibility
    public decimal TargetPercent { get; set; }
    public decimal? CurrentPrice { get; set; }
    public int? FixedShares { get; set; } // If set, use this exact share count instead of calculating from percent
    public decimal EstimatedValue { get; set; }
}

/// <summary>
/// Quote data from E*TRADE
/// </summary>
public class QuoteData
{
    public string Symbol { get; set; } = "";
    public string Description { get; set; } = "";
    public decimal LastPrice { get; set; }
    public decimal Change { get; set; }
    public decimal ChangePercent { get; set; }
    public decimal Bid { get; set; }
    public decimal Ask { get; set; }
    public long Volume { get; set; }
    public decimal High { get; set; }
    public decimal Low { get; set; }
    public decimal Open { get; set; }
    public decimal PreviousClose { get; set; }
}
