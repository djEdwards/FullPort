using Microsoft.AspNetCore.Mvc;
using System.Text.Json;
using System.Security.Cryptography;
using System.Text;
using FullPort.Models;

namespace FullPort.Controllers;

[ApiController]
[Route("api/[controller]")]
public class AnalyticsController : ControllerBase
{
    private readonly IConfiguration _config;
    private readonly ILogger<AnalyticsController> _logger;
    private readonly HttpClient _httpClient;

    private static string? AccessToken => PortfolioController.GetAccessToken();
    private static string? AccessTokenSecret => PortfolioController.GetAccessTokenSecret();

    // Common benchmarks
    private static readonly string[] Benchmarks = ["SPY", "QQQ", "VTI", "AGG"];

    public AnalyticsController(IConfiguration config, ILogger<AnalyticsController> logger)
    {
        _config = config;
        _logger = logger;
        _httpClient = new HttpClient();
    }

    private string ApiBaseUrl => _config.GetValue<bool>("ETrade:UseSandbox")
        ? "https://apisb.etrade.com"
        : "https://api.etrade.com";

    /// <summary>
    /// Get portfolio analytics including concentration, correlation, and benchmark comparison
    /// </summary>
    [HttpGet("portfolio-health")]
    public async Task<IActionResult> GetPortfolioHealth()
    {
        if (string.IsNullOrEmpty(AccessToken))
            return Unauthorized(new { message = "Not authenticated" });

        try
        {
            var holdings = await GetHoldingsWithPrices();
            if (holdings.Count == 0)
                return Ok(new { message = "No holdings found", hasData = false });

            var totalValue = holdings.Sum(h => h.MarketValue);

            // Calculate concentration metrics
            var concentrationAnalysis = CalculateConcentration(holdings, totalValue);

            // Calculate sector exposure (based on asset class for now)
            var sectorExposure = CalculateSectorExposure(holdings, totalValue);

            // Get benchmark quotes for comparison
            var benchmarkData = await GetBenchmarkQuotes();

            // Calculate portfolio health score (0-100)
            var healthScore = CalculateHealthScore(concentrationAnalysis, sectorExposure);

            return Ok(new
            {
                hasData = true,
                totalValue,
                holdingsCount = holdings.Count,
                healthScore,
                concentration = concentrationAnalysis,
                sectorExposure,
                benchmarks = benchmarkData,
                topHoldings = holdings
                    .OrderByDescending(h => h.MarketValue)
                    .Take(10)
                    .Select(h => new
                    {
                        h.Symbol,
                        h.MarketValue,
                        weight = totalValue > 0 ? (h.MarketValue / totalValue) * 100 : 0,
                        h.DayChangePercent
                    })
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error calculating portfolio health");
            return StatusCode(500, new { message = "Error calculating analytics", detail = ex.Message });
        }
    }

    /// <summary>
    /// Get correlation matrix for holdings using historical daily returns
    /// </summary>
    [HttpGet("correlation")]
    public async Task<IActionResult> GetCorrelationMatrix()
    {
        if (string.IsNullOrEmpty(AccessToken))
            return Unauthorized(new { message = "Not authenticated" });

        try
        {
            var holdings = await GetHoldingsWithPrices();
            if (holdings.Count < 2)
                return Ok(new { message = "Need at least 2 holdings for correlation", hasData = false });

            // Get symbols (limit to top 8 by value for cleaner display)
            var symbols = holdings
                .OrderByDescending(h => h.MarketValue)
                .Take(8)
                .Select(h => h.Symbol)
                .ToList();

            // Fetch historical data for correlation calculation
            var historicalData = await GetHistoricalPrices(symbols);

            string method;
            string note;
            List<List<decimal>> matrix;
            int? dataPoints = null;

            if (historicalData.Count >= 2)
            {
                // Use historical correlation
                matrix = CalculateCorrelationMatrix(symbols, historicalData);
                method = "historical";
                dataPoints = historicalData.Values.FirstOrDefault()?.Count ?? 0;
                note = $"Pearson correlation based on {dataPoints} days of daily returns. Values range from -1 (inverse) to +1 (move together).";
            }
            else
            {
                // Fallback: use day change as proxy for correlation direction
                matrix = CalculateDayChangeCorrelation(holdings.Where(h => symbols.Contains(h.Symbol)).ToList());
                method = "dayChange";
                note = "⚠️ Historical data unavailable. Showing estimated correlation based on today's price movements only. This is less reliable than historical data.";
            }

            return Ok(new
            {
                hasData = true,
                method,
                dataPoints,
                symbols,
                matrix,
                note,
                interpretation = new
                {
                    highPositive = "> 0.7: Strong positive correlation - holdings move together",
                    lowPositive = "0.3 to 0.7: Moderate positive correlation",
                    nearZero = "-0.3 to 0.3: Low/no correlation - good for diversification",
                    lowNegative = "-0.7 to -0.3: Moderate negative correlation - some diversification benefit",
                    highNegative = "< -0.7: Strong negative correlation - excellent diversification (rare)"
                }
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error calculating correlation");
            return StatusCode(500, new { message = "Error calculating correlation", detail = ex.Message });
        }
    }

    /// <summary>
    /// Get performance comparison against benchmarks
    /// </summary>
    [HttpGet("benchmark-comparison")]
    public async Task<IActionResult> GetBenchmarkComparison()
    {
        if (string.IsNullOrEmpty(AccessToken))
            return Unauthorized(new { message = "Not authenticated" });

        try
        {
            var holdings = await GetHoldingsWithPrices();
            if (holdings.Count == 0)
                return Ok(new { message = "No holdings found", hasData = false });

            // Calculate portfolio's weighted daily change
            var totalValue = holdings.Sum(h => h.MarketValue);
            var portfolioDayChange = holdings.Sum(h => h.DayChange);
            var portfolioDayChangePercent = totalValue > 0 
                ? (portfolioDayChange / (totalValue - portfolioDayChange)) * 100 
                : 0;

            // Get benchmark performance
            var benchmarkQuotes = await GetBenchmarkQuotes();

            // Calculate relative performance
            var comparison = benchmarkQuotes.Select(b => new
            {
                b.Symbol,
                b.Name,
                b.Price,
                b.DayChangePercent,
                relativePerformance = portfolioDayChangePercent - b.DayChangePercent,
                beating = portfolioDayChangePercent > b.DayChangePercent
            }).ToList();

            return Ok(new
            {
                hasData = true,
                portfolio = new
                {
                    totalValue,
                    dayChange = portfolioDayChange,
                    dayChangePercent = portfolioDayChangePercent
                },
                benchmarks = comparison,
                summary = new
                {
                    beatingCount = comparison.Count(c => c.beating),
                    totalBenchmarks = comparison.Count,
                    bestRelative = comparison.OrderByDescending(c => c.relativePerformance).FirstOrDefault()?.Symbol,
                    worstRelative = comparison.OrderBy(c => c.relativePerformance).FirstOrDefault()?.Symbol
                }
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error calculating benchmark comparison");
            return StatusCode(500, new { message = "Error calculating benchmark comparison", detail = ex.Message });
        }
    }

    #region Private Helper Methods

    private async Task<List<HoldingAnalytics>> GetHoldingsWithPrices()
    {
        var holdings = new List<HoldingAnalytics>();
        var consumerKey = _config["ETrade:ConsumerKey"]!;
        var consumerSecret = _config["ETrade:ConsumerSecret"]!;

        // Get accounts
        var accountKeys = await GetAccountKeys();

        foreach (var acctKey in accountKeys)
        {
            var portfolioUrl = $"{ApiBaseUrl}/v1/accounts/{acctKey}/portfolio.json";
            var (authHeader, _) = OAuthHelper.BuildOAuthHeader(consumerKey, consumerSecret, AccessToken!, AccessTokenSecret!, "GET", portfolioUrl, null);

            using var client = new HttpClient();
            client.DefaultRequestHeaders.Add("Authorization", authHeader);

            var response = await client.GetAsync(portfolioUrl);
            var content = await response.Content.ReadAsStringAsync();

            if (response.IsSuccessStatusCode && !string.IsNullOrWhiteSpace(content))
            {
                try
                {
                    var doc = JsonDocument.Parse(content);
                    if (doc.RootElement.TryGetProperty("PortfolioResponse", out var portfolioResp) &&
                        portfolioResp.TryGetProperty("AccountPortfolio", out var acctPortfolios))
                    {
                        foreach (var acctPortfolio in acctPortfolios.EnumerateArray())
                        {
                            if (acctPortfolio.TryGetProperty("Position", out var positions))
                            {
                                foreach (var pos in positions.EnumerateArray())
                                {
                                    var holding = ParseHolding(pos);
                                    if (holding != null)
                                    {
                                        // Aggregate if same symbol
                                        var existing = holdings.FirstOrDefault(h => h.Symbol == holding.Symbol);
                                        if (existing != null)
                                        {
                                            existing.Quantity += holding.Quantity;
                                            existing.MarketValue += holding.MarketValue;
                                            existing.DayChange += holding.DayChange;
                                        }
                                        else
                                        {
                                            holdings.Add(holding);
                                        }
                                    }
                                }
                            }
                        }
                    }
                }
                catch (JsonException ex)
                {
                    _logger.LogWarning(ex, "Failed to parse portfolio");
                }
            }
        }

        return holdings;
    }

    private HoldingAnalytics? ParseHolding(JsonElement pos)
    {
        try
        {
            var holding = new HoldingAnalytics();

            if (pos.TryGetProperty("Product", out var product) &&
                product.TryGetProperty("symbol", out var symbol))
            {
                holding.Symbol = symbol.GetString() ?? "";
            }

            if (pos.TryGetProperty("quantity", out var qty))
                holding.Quantity = qty.GetDecimal();

            if (pos.TryGetProperty("marketValue", out var mv))
                holding.MarketValue = mv.GetDecimal();

            if (pos.TryGetProperty("totalGain", out var gain))
                holding.TotalGain = gain.GetDecimal();

            if (pos.TryGetProperty("totalGainPct", out var gainPct))
                holding.TotalGainPercent = gainPct.GetDecimal();

            if (pos.TryGetProperty("Quick", out var quick))
            {
                if (quick.TryGetProperty("lastTrade", out var price))
                    holding.Price = price.GetDecimal();
                if (quick.TryGetProperty("change", out var change))
                    holding.DayChange = change.GetDecimal() * holding.Quantity;
                if (quick.TryGetProperty("changePct", out var changePct))
                    holding.DayChangePercent = changePct.GetDecimal();
            }

            holding.AssetClass = AssetClassMappings.GetAssetClass(holding.Symbol).ToString();

            return string.IsNullOrEmpty(holding.Symbol) ? null : holding;
        }
        catch
        {
            return null;
        }
    }

    private async Task<List<string>> GetAccountKeys()
    {
        var consumerKey = _config["ETrade:ConsumerKey"]!;
        var consumerSecret = _config["ETrade:ConsumerSecret"]!;
        var accountKeys = new List<string>();

        var listUrl = $"{ApiBaseUrl}/v1/accounts/list.json";
        var (authHeader, _) = OAuthHelper.BuildOAuthHeader(consumerKey, consumerSecret, AccessToken!, AccessTokenSecret!, "GET", listUrl, null);

        using var client = new HttpClient();
        client.DefaultRequestHeaders.Add("Authorization", authHeader);

        var response = await client.GetAsync(listUrl);
        var content = await response.Content.ReadAsStringAsync();

        if (response.IsSuccessStatusCode)
        {
            var doc = JsonDocument.Parse(content);
            if (doc.RootElement.TryGetProperty("AccountListResponse", out var resp) &&
                resp.TryGetProperty("Accounts", out var accts) &&
                accts.TryGetProperty("Account", out var acctArr))
            {
                foreach (var acct in acctArr.EnumerateArray())
                {
                    if (acct.TryGetProperty("accountIdKey", out var key) &&
                        acct.TryGetProperty("accountStatus", out var status) &&
                        status.GetString() == "ACTIVE")
                    {
                        accountKeys.Add(key.GetString()!);
                    }
                }
            }
        }

        return accountKeys;
    }

    private async Task<List<BenchmarkQuote>> GetBenchmarkQuotes()
    {
        var quotes = new List<BenchmarkQuote>();
        var consumerKey = _config["ETrade:ConsumerKey"]!;
        var consumerSecret = _config["ETrade:ConsumerSecret"]!;

        var symbolsParam = string.Join(",", Benchmarks);
        var quoteUrl = $"{ApiBaseUrl}/v1/market/quote/{symbolsParam}.json";

        var (authHeader, _) = OAuthHelper.BuildOAuthHeader(consumerKey, consumerSecret, AccessToken!, AccessTokenSecret!, "GET", quoteUrl, null);

        using var client = new HttpClient();
        client.DefaultRequestHeaders.Add("Authorization", authHeader);

        var response = await client.GetAsync(quoteUrl);
        var content = await response.Content.ReadAsStringAsync();

        if (response.IsSuccessStatusCode)
        {
            try
            {
                var doc = JsonDocument.Parse(content);
                if (doc.RootElement.TryGetProperty("QuoteResponse", out var quoteResp) &&
                    quoteResp.TryGetProperty("QuoteData", out var quoteData))
                {
                    foreach (var quote in quoteData.EnumerateArray())
                    {
                        var bq = new BenchmarkQuote();

                        if (quote.TryGetProperty("Product", out var product) &&
                            product.TryGetProperty("symbol", out var symbol))
                        {
                            bq.Symbol = symbol.GetString() ?? "";
                        }

                        if (quote.TryGetProperty("All", out var all))
                        {
                            if (all.TryGetProperty("lastTrade", out var price))
                                bq.Price = price.GetDecimal();
                            if (all.TryGetProperty("changeClose", out var change))
                                bq.DayChange = change.GetDecimal();
                            if (all.TryGetProperty("changeClosePercentage", out var changePct))
                                bq.DayChangePercent = changePct.GetDecimal();
                        }

                        // Set friendly names
                        bq.Name = bq.Symbol switch
                        {
                            "SPY" => "S&P 500",
                            "QQQ" => "Nasdaq 100",
                            "VTI" => "Total US Market",
                            "AGG" => "US Bonds",
                            _ => bq.Symbol
                        };

                        if (!string.IsNullOrEmpty(bq.Symbol))
                            quotes.Add(bq);
                    }
                }
            }
            catch (JsonException ex)
            {
                _logger.LogWarning(ex, "Failed to parse benchmark quotes");
            }
        }

        return quotes;
    }

    private async Task<Dictionary<string, List<decimal>>> GetHistoricalPrices(List<string> symbols)
    {
        var historicalData = new Dictionary<string, List<decimal>>();

        // Use Yahoo Finance API for historical data (free, no auth required)
        // Get 60 days of daily returns for correlation calculation
        var endDate = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var startDate = DateTimeOffset.UtcNow.AddDays(-90).ToUnixTimeSeconds();

        foreach (var symbol in symbols)
        {
            try
            {
                using var client = new HttpClient();
                client.DefaultRequestHeaders.Add("User-Agent", "Mozilla/5.0");

                // Yahoo Finance v8 API endpoint
                var url = $"https://query1.finance.yahoo.com/v8/finance/chart/{symbol}?period1={startDate}&period2={endDate}&interval=1d";

                var response = await client.GetAsync(url);
                if (!response.IsSuccessStatusCode)
                {
                    _logger.LogWarning("Failed to get historical data for {Symbol}: {Status}", symbol, response.StatusCode);
                    continue;
                }

                var content = await response.Content.ReadAsStringAsync();
                var doc = JsonDocument.Parse(content);

                // Parse Yahoo Finance response
                if (doc.RootElement.TryGetProperty("chart", out var chart) &&
                    chart.TryGetProperty("result", out var results) &&
                    results.GetArrayLength() > 0)
                {
                    var result = results[0];
                    if (result.TryGetProperty("indicators", out var indicators) &&
                        indicators.TryGetProperty("adjclose", out var adjclose) &&
                        adjclose.GetArrayLength() > 0)
                    {
                        var prices = adjclose[0].GetProperty("adjclose");
                        var priceList = new List<decimal>();

                        foreach (var price in prices.EnumerateArray())
                        {
                            if (price.ValueKind == JsonValueKind.Number)
                            {
                                priceList.Add(price.GetDecimal());
                            }
                        }

                        // Calculate daily returns (not prices) for correlation
                        if (priceList.Count > 1)
                        {
                            var returns = new List<decimal>();
                            for (int i = 1; i < priceList.Count; i++)
                            {
                                if (priceList[i - 1] != 0)
                                {
                                    var dailyReturn = (priceList[i] - priceList[i - 1]) / priceList[i - 1];
                                    returns.Add(dailyReturn);
                                }
                            }

                            if (returns.Count >= 20) // Need at least 20 data points for meaningful correlation
                            {
                                historicalData[symbol] = returns;
                                _logger.LogInformation("Got {Count} days of returns for {Symbol}", returns.Count, symbol);
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error fetching historical data for {Symbol}", symbol);
            }
        }

        return historicalData;
    }

    private ConcentrationAnalysis CalculateConcentration(List<HoldingAnalytics> holdings, decimal totalValue)
    {
        if (totalValue == 0 || holdings.Count == 0)
            return new ConcentrationAnalysis();

        var sorted = holdings.OrderByDescending(h => h.MarketValue).ToList();
        var topHolding = sorted.First();
        var top5Value = sorted.Take(5).Sum(h => h.MarketValue);
        var top10Value = sorted.Take(10).Sum(h => h.MarketValue);

        // Herfindahl-Hirschman Index (HHI) - measure of concentration
        // Sum of squared weights, lower = more diversified
        var hhi = holdings.Sum(h => Math.Pow((double)(h.MarketValue / totalValue), 2));
        
        // Effective number of holdings (inverse of HHI)
        var effectiveHoldings = hhi > 0 ? 1 / hhi : holdings.Count;

        return new ConcentrationAnalysis
        {
            TopHoldingSymbol = topHolding.Symbol,
            TopHoldingPercent = (topHolding.MarketValue / totalValue) * 100,
            Top5Percent = (top5Value / totalValue) * 100,
            Top10Percent = (top10Value / totalValue) * 100,
            HHI = (decimal)hhi,
            EffectiveHoldings = (decimal)effectiveHoldings,
            IsConcentrated = (topHolding.MarketValue / totalValue) > 0.25m, // >25% in one stock
            ConcentrationWarnings = GenerateConcentrationWarnings(holdings, totalValue)
        };
    }

    private List<string> GenerateConcentrationWarnings(List<HoldingAnalytics> holdings, decimal totalValue)
    {
        var warnings = new List<string>();

        foreach (var h in holdings)
        {
            var pct = (h.MarketValue / totalValue) * 100;
            if (pct > 25)
                warnings.Add($"⚠️ {h.Symbol} is {pct:F1}% of portfolio - consider reducing for diversification");
            else if (pct > 15)
                warnings.Add($"📊 {h.Symbol} is {pct:F1}% of portfolio - moderate concentration");
        }

        if (holdings.Count < 5)
            warnings.Add("📈 Consider adding more positions for better diversification");

        return warnings;
    }

    private List<SectorExposure> CalculateSectorExposure(List<HoldingAnalytics> holdings, decimal totalValue)
    {
        return holdings
            .GroupBy(h => h.AssetClass)
            .Select(g => new SectorExposure
            {
                Sector = g.Key,
                Value = g.Sum(h => h.MarketValue),
                Percent = totalValue > 0 ? (g.Sum(h => h.MarketValue) / totalValue) * 100 : 0,
                HoldingsCount = g.Count()
            })
            .OrderByDescending(s => s.Value)
            .ToList();
    }

    private decimal CalculateHealthScore(ConcentrationAnalysis concentration, List<SectorExposure> sectors)
    {
        // Start with 100 and deduct for issues
        decimal score = 100;

        // Deduct for concentration
        if (concentration.TopHoldingPercent > 50) score -= 30;
        else if (concentration.TopHoldingPercent > 25) score -= 15;
        else if (concentration.TopHoldingPercent > 15) score -= 5;

        // Deduct for lack of diversification
        if (concentration.EffectiveHoldings < 3) score -= 20;
        else if (concentration.EffectiveHoldings < 5) score -= 10;
        else if (concentration.EffectiveHoldings < 10) score -= 5;

        // Deduct for sector concentration
        var topSector = sectors.FirstOrDefault();
        if (topSector != null && topSector.Percent > 80) score -= 15;
        else if (topSector != null && topSector.Percent > 60) score -= 10;

        // Bonus for having multiple asset classes
        if (sectors.Count >= 4) score += 5;
        else if (sectors.Count >= 3) score += 2;

        return Math.Clamp(score, 0, 100);
    }

    private List<List<decimal>> CalculateCorrelationMatrix(List<string> symbols, Dictionary<string, List<decimal>> historicalData)
    {
        // Calculate Pearson correlation from historical returns
        var n = symbols.Count;
        var matrix = new List<List<decimal>>();

        for (int i = 0; i < n; i++)
        {
            var row = new List<decimal>();
            for (int j = 0; j < n; j++)
            {
                if (i == j)
                {
                    row.Add(1.0m);
                }
                else if (historicalData.TryGetValue(symbols[i], out var returns1) &&
                         historicalData.TryGetValue(symbols[j], out var returns2))
                {
                    row.Add(CalculatePearsonCorrelation(returns1, returns2));
                }
                else
                {
                    row.Add(0m);
                }
            }
            matrix.Add(row);
        }

        return matrix;
    }

    private decimal CalculatePearsonCorrelation(List<decimal> x, List<decimal> y)
    {
        var n = Math.Min(x.Count, y.Count);
        if (n < 10) return 0; // Need sufficient data for meaningful correlation

        // Use the last n values
        var xValues = x.TakeLast(n).ToList();
        var yValues = y.TakeLast(n).ToList();

        var avgX = xValues.Average();
        var avgY = yValues.Average();

        decimal sumXY = 0, sumX2 = 0, sumY2 = 0;

        for (int i = 0; i < n; i++)
        {
            var dx = xValues[i] - avgX;
            var dy = yValues[i] - avgY;
            sumXY += dx * dy;
            sumX2 += dx * dx;
            sumY2 += dy * dy;
        }

        var denominator = (double)(sumX2 * sumY2);
        if (denominator <= 0) return 0;

        var correlation = (decimal)(((double)sumXY) / Math.Sqrt(denominator));

        // Round to 2 decimal places for cleaner display
        return Math.Round(Math.Clamp(correlation, -1, 1), 2);
    }

    /// <summary>
    /// Improved day-change correlation fallback with better estimation
    /// Uses a simple estimation when historical data is unavailable
    /// </summary>
    private List<List<decimal>> CalculateDayChangeCorrelation(List<HoldingAnalytics> holdings)
    {
        var n = holdings.Count;
        var matrix = new List<List<decimal>>();

        for (int i = 0; i < n; i++)
        {
            var row = new List<decimal>();
            for (int j = 0; j < n; j++)
            {
                if (i == j)
                {
                    row.Add(1.0m);
                }
                else
                {
                    var change1 = holdings[i].DayChangePercent;
                    var change2 = holdings[j].DayChangePercent;

                    // Calculate correlation estimate based on:
                    // 1. Same direction = positive correlation tendency
                    // 2. Magnitude similarity = higher correlation
                    // 3. Apply dampening factor since this is just 1 day of data

                    if (Math.Abs(change1) < 0.01m || Math.Abs(change2) < 0.01m)
                    {
                        // One or both barely moved - assume low/no correlation
                        row.Add(0m);
                    }
                    else
                    {
                        var sameDirection = Math.Sign(change1) == Math.Sign(change2);

                        // Calculate how similar the magnitudes are (0 to 1)
                        var maxMag = Math.Max(Math.Abs(change1), Math.Abs(change2));
                        var minMag = Math.Min(Math.Abs(change1), Math.Abs(change2));
                        var magnitudeSimilarity = minMag / maxMag;

                        // Base correlation from direction and magnitude
                        var rawCorrelation = magnitudeSimilarity * (sameDirection ? 0.8m : -0.6m);

                        // Add some noise dampening (single day is unreliable)
                        // Max absolute value is 0.8 for same direction, -0.6 for opposite
                        var correlation = Math.Round(Math.Clamp(rawCorrelation, -0.8m, 0.8m), 2);

                        row.Add(correlation);
                    }
                }
            }
            matrix.Add(row);
        }

        return matrix;
    }

    #endregion
}

#region Analytics Models

public class HoldingAnalytics
{
    public string Symbol { get; set; } = "";
    public decimal Quantity { get; set; }
    public decimal Price { get; set; }
    public decimal MarketValue { get; set; }
    public decimal TotalGain { get; set; }
    public decimal TotalGainPercent { get; set; }
    public decimal DayChange { get; set; }
    public decimal DayChangePercent { get; set; }
    public string AssetClass { get; set; } = "";
}

public class BenchmarkQuote
{
    public string Symbol { get; set; } = "";
    public string Name { get; set; } = "";
    public decimal Price { get; set; }
    public decimal DayChange { get; set; }
    public decimal DayChangePercent { get; set; }
}

public class ConcentrationAnalysis
{
    public string TopHoldingSymbol { get; set; } = "";
    public decimal TopHoldingPercent { get; set; }
    public decimal Top5Percent { get; set; }
    public decimal Top10Percent { get; set; }
    public decimal HHI { get; set; }
    public decimal EffectiveHoldings { get; set; }
    public bool IsConcentrated { get; set; }
    public List<string> ConcentrationWarnings { get; set; } = [];
}

public class SectorExposure
{
    public string Sector { get; set; } = "";
    public decimal Value { get; set; }
    public decimal Percent { get; set; }
    public int HoldingsCount { get; set; }
}

#endregion

#region OAuth Helper (shared)

public static class OAuthHelper
{
    public static (string Header, string Signature) BuildOAuthHeader(
        string consumerKey, string consumerSecret,
        string accessToken, string accessTokenSecret,
        string method, string url, SortedDictionary<string, string>? queryParams)
    {
        var timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString();
        var nonce = Guid.NewGuid().ToString("N");

        var oauthParams = new SortedDictionary<string, string>
        {
            { "oauth_consumer_key", consumerKey },
            { "oauth_token", accessToken },
            { "oauth_signature_method", "HMAC-SHA1" },
            { "oauth_timestamp", timestamp },
            { "oauth_nonce", nonce },
            { "oauth_version", "1.0" }
        };

        var allParams = new SortedDictionary<string, string>(oauthParams);
        if (queryParams != null)
        {
            foreach (var kvp in queryParams)
                allParams[kvp.Key] = kvp.Value;
        }

        var paramString = string.Join("&", allParams.Select(kvp =>
            $"{Uri.EscapeDataString(kvp.Key)}={Uri.EscapeDataString(kvp.Value)}"));

        var baseString = $"{method}&{Uri.EscapeDataString(url)}&{Uri.EscapeDataString(paramString)}";
        var signingKey = $"{Uri.EscapeDataString(consumerSecret)}&{Uri.EscapeDataString(accessTokenSecret)}";

        using var hasher = new HMACSHA1(Encoding.UTF8.GetBytes(signingKey));
        var signatureBytes = hasher.ComputeHash(Encoding.UTF8.GetBytes(baseString));
        var signature = Convert.ToBase64String(signatureBytes);

        oauthParams["oauth_signature"] = signature;

        var header = "OAuth " + string.Join(", ", oauthParams.Select(kvp =>
            $"{Uri.EscapeDataString(kvp.Key)}=\"{Uri.EscapeDataString(kvp.Value)}\""));

        return (header, signature);
    }
}

#endregion
