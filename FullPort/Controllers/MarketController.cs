using Microsoft.AspNetCore.Mvc;
using System.Text.Json;
using System.Security.Cryptography;
using System.Text;
using FullPort.Models;

namespace FullPort.Controllers;

[ApiController]
[Route("api/[controller]")]
public class MarketController : ControllerBase
{
    private readonly IConfiguration _config;
    private readonly ILogger<MarketController> _logger;

    private static string? AccessToken => PortfolioController.GetAccessToken();
    private static string? AccessTokenSecret => PortfolioController.GetAccessTokenSecret();

    public MarketController(IConfiguration config, ILogger<MarketController> logger)
    {
        _config = config;
        _logger = logger;
    }

    private string ApiBaseUrl => _config.GetValue<bool>("ETrade:UseSandbox")
        ? "https://apisb.etrade.com"
        : "https://api.etrade.com";

    /// <summary>
    /// Get real-time quotes for one or more symbols
    /// </summary>
    [HttpGet("quote/{symbols}")]
    public async Task<IActionResult> GetQuotes(string symbols)
    {
        if (string.IsNullOrEmpty(AccessToken) || string.IsNullOrEmpty(AccessTokenSecret))
            return Unauthorized(new { message = "Not authenticated" });

        try
        {
            var consumerKey = _config["ETrade:ConsumerKey"]!;
            var consumerSecret = _config["ETrade:ConsumerSecret"]!;

            var symbolList = symbols.ToUpper().Split(',').Select(s => s.Trim()).ToList();
            var quotes = new List<QuoteData>();

            // E*TRADE allows up to 25 symbols per request
            var batches = symbolList.Chunk(25);

            foreach (var batch in batches)
            {
                var symbolsParam = string.Join(",", batch);
                var baseUrl = $"{ApiBaseUrl}/v1/market/quote/{symbolsParam}.json";

                using var client = new HttpClient();
                var (authHeader, _) = BuildOAuthHeader(consumerKey, consumerSecret, AccessToken!, AccessTokenSecret!, "GET", baseUrl, null);
                client.DefaultRequestHeaders.Add("Authorization", authHeader);

                var response = await client.GetAsync(baseUrl);
                var content = await response.Content.ReadAsStringAsync();

                _logger.LogInformation("Quote response for {Symbols}: {Status}", symbolsParam, response.StatusCode);

                if (response.IsSuccessStatusCode && !string.IsNullOrWhiteSpace(content))
                {
                    try
                    {
                        var doc = JsonDocument.Parse(content);
                        if (doc.RootElement.TryGetProperty("QuoteResponse", out var quoteResp) &&
                            quoteResp.TryGetProperty("QuoteData", out var quoteData))
                        {
                            foreach (var quote in quoteData.EnumerateArray())
                            {
                                var q = ParseQuote(quote);
                                if (q != null)
                                    quotes.Add(q);
                            }
                        }
                    }
                    catch (JsonException ex)
                    {
                        _logger.LogWarning(ex, "Failed to parse quote response: {Content}", content);
                    }
                }
                else
                {
                    _logger.LogWarning("Quote request failed: {Status} {Content}", response.StatusCode, content);
                }
            }

            return Ok(quotes);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting quotes");
            return StatusCode(500, new { message = "Error getting quotes", detail = ex.Message });
        }
    }

    /// <summary>
    /// Get major market indices (S&P 500, Dow, Nasdaq, etc.)
    /// </summary>
    [HttpGet("indices")]
    public async Task<IActionResult> GetMarketIndices()
    {
        // Return cached/mock data if not authenticated (indices are public info)
        var indices = new List<object>();

        try
        {
            // Try to get real quotes if authenticated
            if (!string.IsNullOrEmpty(AccessToken) && !string.IsNullOrEmpty(AccessTokenSecret))
            {
                var quotes = await GetQuotesInternal("SPY,QQQ,DIA,IWM,VTI");

                foreach (var q in quotes)
                {
                    var indexName = q.Symbol switch
                    {
                        "SPY" => "S&P 500",
                        "QQQ" => "Nasdaq 100",
                        "DIA" => "Dow Jones",
                        "IWM" => "Russell 2000",
                        "VTI" => "Total Market",
                        _ => q.Symbol
                    };

                    indices.Add(new
                    {
                        symbol = q.Symbol,
                        name = indexName,
                        price = q.LastPrice,
                        change = q.Change,
                        changePercent = q.ChangePercent,
                        isUp = q.Change >= 0
                    });
                }
            }

            // If no real data, return placeholder structure
            if (indices.Count == 0)
            {
                indices = new List<object>
                {
                    new { symbol = "SPY", name = "S&P 500", price = 0, change = 0, changePercent = 0, isUp = true, noData = true },
                    new { symbol = "QQQ", name = "Nasdaq 100", price = 0, change = 0, changePercent = 0, isUp = true, noData = true },
                    new { symbol = "DIA", name = "Dow Jones", price = 0, change = 0, changePercent = 0, isUp = true, noData = true },
                    new { symbol = "IWM", name = "Russell 2000", price = 0, change = 0, changePercent = 0, isUp = true, noData = true }
                };
            }

            return Ok(new { indices, timestamp = DateTime.UtcNow });
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error fetching market indices");
            return Ok(new { indices = new List<object>(), error = "Could not fetch market data" });
        }
    }

    /// <summary>
    /// Get suggested ETFs with real-time prices
    /// </summary>
    [HttpGet("suggested-etfs")]
    public async Task<IActionResult> GetSuggestedETFs()
    {
        if (string.IsNullOrEmpty(AccessToken) || string.IsNullOrEmpty(AccessTokenSecret))
            return Unauthorized(new { message = "Not authenticated" });

        try
        {
            _logger.LogInformation("Getting suggested ETFs...");
            var result = new Dictionary<string, List<SuggestedETF>>();

            foreach (var kvp in AssetClassMappings.SuggestedETFs)
            {
                var assetClass = kvp.Key;
                var etfs = kvp.Value.Select(e => new SuggestedETF(e.Symbol, e.Name, e.Description)).ToList();

                // Get quotes for these ETFs
                var symbols = string.Join(",", etfs.Select(e => e.Symbol));
                _logger.LogInformation("Fetching quotes for {AssetClass}: {Symbols}", assetClass, symbols);

                try
                {
                    var quotes = await GetQuotesInternal(symbols);
                    _logger.LogInformation("Got {Count} quotes for {AssetClass}", quotes.Count, assetClass);

                    foreach (var etf in etfs)
                    {
                        var quote = quotes.FirstOrDefault(q => q.Symbol.Equals(etf.Symbol, StringComparison.OrdinalIgnoreCase));
                        if (quote != null)
                        {
                            etf.CurrentPrice = quote.LastPrice;
                            etf.ChangePercent = quote.ChangePercent;
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to get quotes for {AssetClass}, continuing without prices", assetClass);
                }

                result[assetClass.ToString()] = etfs;
            }

            _logger.LogInformation("Returning {Count} asset classes with ETFs", result.Count);
            return Ok(result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting suggested ETFs");
            return StatusCode(500, new { message = "Error getting suggested ETFs", detail = ex.Message });
        }
    }

    /// <summary>
    /// Build a portfolio based on available cash and selected model
    /// </summary>
    [HttpPost("build-portfolio")]
    public async Task<IActionResult> BuildPortfolio([FromBody] BuildPortfolioRequest? request)
    {
        if (request == null)
        {
            _logger.LogWarning("Build portfolio request is null");
            return BadRequest(new { message = "Request body is required" });
        }

        _logger.LogInformation("Building portfolio: CashToInvest={Cash}, Items={Count}", 
            request.CashToInvest, request.Items?.Count ?? 0);

        if (string.IsNullOrEmpty(AccessToken) || string.IsNullOrEmpty(AccessTokenSecret))
            return Unauthorized(new { message = "Not authenticated" });

        if (request.Items == null || request.Items.Count == 0)
            return BadRequest(new { message = "No items in portfolio request" });

        try
        {
            var cashToInvest = request.CashToInvest * (1 - request.CashReservePercent / 100);
            var trades = new List<RebalanceTrade>();

            // Get current prices for all symbols
            var symbols = string.Join(",", request.Items.Select(i => i.Symbol));
            _logger.LogInformation("Getting quotes for symbols: {Symbols}", symbols);
            var quotes = await GetQuotesInternal(symbols);

            foreach (var item in request.Items)
            {
                var quote = quotes.FirstOrDefault(q => q.Symbol.Equals(item.Symbol, StringComparison.OrdinalIgnoreCase));
                if (quote == null || quote.LastPrice <= 0) continue;

                int quantity;
                if (item.FixedShares.HasValue && item.FixedShares.Value > 0)
                {
                    // Use the exact number of shares specified by the user
                    quantity = item.FixedShares.Value;
                    _logger.LogInformation("Using fixed shares for {Symbol}: {Quantity}", item.Symbol, quantity);
                }
                else
                {
                    // Calculate shares from target percentage
                    var targetValue = cashToInvest * (item.TargetPercent / 100);
                    quantity = (int)Math.Floor(targetValue / quote.LastPrice);
                    _logger.LogInformation("Calculated shares for {Symbol}: {Quantity} (targetValue={TargetValue}, price={Price})", 
                        item.Symbol, quantity, targetValue, quote.LastPrice);
                }

                if (quantity > 0)
                {
                    trades.Add(new RebalanceTrade
                    {
                        Symbol = item.Symbol,
                        Description = quote.Description,
                        Action = "BUY",
                        Quantity = quantity,
                        EstimatedPrice = quote.LastPrice,
                        EstimatedValue = quantity * quote.LastPrice,
                        AccountIdKey = request.AccountIdKey ?? ""
                    });
                }
            }

            var totalInvestment = trades.Sum(t => t.EstimatedValue);
            var remainingCash = request.CashToInvest - totalInvestment;

            return Ok(new
            {
                trades,
                summary = new
                {
                    totalCash = request.CashToInvest,
                    cashReserve = request.CashToInvest * (request.CashReservePercent / 100),
                    totalInvestment,
                    remainingCash,
                    tradeCount = trades.Count
                }
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error building portfolio");
            return StatusCode(500, new { message = "Error building portfolio", detail = ex.Message });
        }
    }

    private async Task<List<QuoteData>> GetQuotesInternal(string symbols)
    {
        var consumerKey = _config["ETrade:ConsumerKey"]!;
        var consumerSecret = _config["ETrade:ConsumerSecret"]!;

        var quotes = new List<QuoteData>();
        var symbolList = symbols.ToUpper().Split(',').Select(s => s.Trim()).ToList();
        var batches = symbolList.Chunk(25);

        foreach (var batch in batches)
        {
            var symbolsParam = string.Join(",", batch);
            var baseUrl = $"{ApiBaseUrl}/v1/market/quote/{symbolsParam}.json";

            using var client = new HttpClient();
            var (authHeader, _) = BuildOAuthHeader(consumerKey, consumerSecret, AccessToken!, AccessTokenSecret!, "GET", baseUrl, null);
            client.DefaultRequestHeaders.Add("Authorization", authHeader);

            var response = await client.GetAsync(baseUrl);
            var content = await response.Content.ReadAsStringAsync();

            if (response.IsSuccessStatusCode && !string.IsNullOrWhiteSpace(content))
            {
                try
                {
                    var doc = JsonDocument.Parse(content);
                    if (doc.RootElement.TryGetProperty("QuoteResponse", out var quoteResp) &&
                        quoteResp.TryGetProperty("QuoteData", out var quoteData))
                    {
                        foreach (var quote in quoteData.EnumerateArray())
                        {
                            var q = ParseQuote(quote);
                            if (q != null)
                                quotes.Add(q);
                        }
                    }
                }
                catch (JsonException ex)
                {
                    _logger.LogWarning(ex, "Failed to parse quote response");
                }
            }
        }

        return quotes;
    }

    private QuoteData? ParseQuote(JsonElement quote)
    {
        try
        {
            var q = new QuoteData();

            if (quote.TryGetProperty("Product", out var product) &&
                product.TryGetProperty("symbol", out var symbol))
            {
                q.Symbol = symbol.GetString() ?? "";
            }

            if (quote.TryGetProperty("All", out var all))
            {
                q.LastPrice = TryGetDecimal(all, "lastTrade") ?? 0;
                q.Change = TryGetDecimal(all, "changeClose") ?? 0;
                q.ChangePercent = TryGetDecimal(all, "changeClosePercentage") ?? 0;
                q.Bid = TryGetDecimal(all, "bid") ?? 0;
                q.Ask = TryGetDecimal(all, "ask") ?? 0;
                q.Volume = TryGetLong(all, "totalVolume") ?? 0;
                q.High = TryGetDecimal(all, "high") ?? 0;
                q.Low = TryGetDecimal(all, "low") ?? 0;
                q.Open = TryGetDecimal(all, "open") ?? 0;
                q.PreviousClose = TryGetDecimal(all, "previousClose") ?? 0;

                if (all.TryGetProperty("symbolDescription", out var desc))
                    q.Description = desc.GetString() ?? "";
            }

            return string.IsNullOrEmpty(q.Symbol) ? null : q;
        }
        catch
        {
            return null;
        }
    }

    private static decimal? TryGetDecimal(JsonElement element, string propertyName)
    {
        if (element.TryGetProperty(propertyName, out var prop))
        {
            if (prop.ValueKind == JsonValueKind.Number)
                return prop.GetDecimal();
            if (prop.ValueKind == JsonValueKind.String && decimal.TryParse(prop.GetString(), out var val))
                return val;
        }
        return null;
    }

    private static long? TryGetLong(JsonElement element, string propertyName)
    {
        if (element.TryGetProperty(propertyName, out var prop))
        {
            if (prop.ValueKind == JsonValueKind.Number)
                return prop.GetInt64();
        }
        return null;
    }

    private (string authHeader, string signature) BuildOAuthHeader(
        string consumerKey, string consumerSecret,
        string token, string tokenSecret,
        string method, string url,
        SortedDictionary<string, string>? queryParams)
    {
        var oauthNonce = Guid.NewGuid().ToString("N");
        var oauthTimestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString();

        var oauthParams = new SortedDictionary<string, string>
        {
            { "oauth_consumer_key", consumerKey },
            { "oauth_nonce", oauthNonce },
            { "oauth_signature_method", "HMAC-SHA1" },
            { "oauth_timestamp", oauthTimestamp },
            { "oauth_token", token },
            { "oauth_version", "1.0" }
        };

        var allParams = new SortedDictionary<string, string>(oauthParams);
        if (queryParams != null)
        {
            foreach (var kvp in queryParams)
                allParams[kvp.Key] = kvp.Value;
        }

        var baseString = $"{method}&" + Uri.EscapeDataString(url) + "&" +
            Uri.EscapeDataString(string.Join("&", allParams.Select(kvp => $"{Uri.EscapeDataString(kvp.Key)}={Uri.EscapeDataString(kvp.Value)}")));
        var signingKey = $"{Uri.EscapeDataString(consumerSecret)}&{Uri.EscapeDataString(tokenSecret)}";

        using var hasher = new HMACSHA1(Encoding.ASCII.GetBytes(signingKey));
        var signature = Convert.ToBase64String(hasher.ComputeHash(Encoding.ASCII.GetBytes(baseString)));

        var headerParams = new List<string>();
        foreach (var kvp in oauthParams)
            headerParams.Add($"{kvp.Key}=\"{Uri.EscapeDataString(kvp.Value)}\"");
        headerParams.Add($"oauth_signature=\"{Uri.EscapeDataString(signature)}\"");

        return ("OAuth " + string.Join(", ", headerParams), signature);
    }
}
