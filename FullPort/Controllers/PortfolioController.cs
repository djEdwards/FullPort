using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Threading.Tasks;
using System.Collections.Concurrent;
using System.Web;
using System.Text;
using System.Security.Cryptography;

namespace FullPort.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class PortfolioController : ControllerBase
    {
        private readonly IConfiguration _config;
        private readonly ILogger<PortfolioController> _logger;
        private static readonly ConcurrentDictionary<string, string> _requestTokens = new();
        private static string? _accessToken;
        private static string? _accessTokenSecret;

        // Expose tokens for other controllers (in production, use proper DI/session management)
        public static string? GetAccessToken() => _accessToken;
        public static string? GetAccessTokenSecret() => _accessTokenSecret;

        public PortfolioController(IConfiguration config, ILogger<PortfolioController> logger)
        {
            _config = config;
            _logger = logger;
        }

        private string ApiBaseUrl => _config.GetValue<bool>("ETrade:UseSandbox") 
            ? "https://apisb.etrade.com" 
            : "https://api.etrade.com";

        [HttpGet]
        public async Task<IActionResult> GetPortfolio()
        {
            try
            {
                if (string.IsNullOrEmpty(_accessToken) || string.IsNullOrEmpty(_accessTokenSecret))
                {
                    var authUrl = Url.Action("Authenticate", "Portfolio", null, Request.Scheme);
                    return Unauthorized(new { message = "Not authenticated", authUrl });
                }
                var consumerKey = _config["ETrade:ConsumerKey"];
                var consumerSecret = _config["ETrade:ConsumerSecret"];
                var apiBase = $"{ApiBaseUrl}/v1/accounts";

                using var client = new HttpClient();

                // 1. Get account list
                var listUrl = $"{apiBase}/list.json";
                var (listAuthHeader, _) = BuildOAuthHeader(consumerKey!, consumerSecret!, _accessToken, _accessTokenSecret, "GET", listUrl, null);
                client.DefaultRequestHeaders.Clear();
                client.DefaultRequestHeaders.Add("Authorization", listAuthHeader);
                var resp = await client.GetAsync(listUrl);
                var content = await resp.Content.ReadAsStringAsync();

                if (!resp.IsSuccessStatusCode)
                {
                    _logger.LogError("E*TRADE API error: {Status} {Content}", resp.StatusCode, content);
                    if (content.TrimStart().StartsWith("<!DOCTYPE html", StringComparison.OrdinalIgnoreCase))
                        return StatusCode((int)resp.StatusCode, new { message = "E*TRADE API returned an HTML error page. Check your credentials, permissions, or try again later.", html = content });
                    return StatusCode((int)resp.StatusCode, content);
                }
                if (content.TrimStart().StartsWith("<!DOCTYPE html", StringComparison.OrdinalIgnoreCase))
                    return StatusCode(502, new { message = "E*TRADE API returned an HTML error page. Check your credentials, permissions, or try again later.", html = content });

                _logger.LogInformation("Account list response: {Content}", content);

                var listJson = JsonDocument.Parse(content);
                var accountsList = new List<JsonElement>();

                if (listJson.RootElement.TryGetProperty("AccountListResponse", out var respElem) &&
                    respElem.TryGetProperty("Accounts", out var acctsElem) &&
                    acctsElem.TryGetProperty("Account", out var acctArrElem) &&
                    acctArrElem.ValueKind == JsonValueKind.Array)
                {
                    accountsList = acctArrElem.EnumerateArray().ToList();
                }
                else if (listJson.RootElement.TryGetProperty("accounts", out var accountsElem) && accountsElem.ValueKind == JsonValueKind.Array)
                {
                    accountsList = accountsElem.EnumerateArray().ToList();
                }
                else
                {
                    return Ok(new { message = "No accounts found or invalid response from E*TRADE.", raw = listJson.RootElement.ToString() });
                }

                _logger.LogInformation("Found {Count} accounts before dedup", accountsList.Count);

                // Deduplicate accounts by accountIdKey
                var seenAccountKeys = new HashSet<string>();
                var uniqueAccounts = new List<JsonElement>();
                foreach (var acct in accountsList)
                {
                    string? key = null;
                    if (acct.TryGetProperty("accountIdKey", out var keyElem) && keyElem.ValueKind == JsonValueKind.String)
                        key = keyElem.GetString();
                    else if (acct.TryGetProperty("accountId", out var idElem) && idElem.ValueKind == JsonValueKind.String)
                        key = idElem.GetString();

                    if (!string.IsNullOrEmpty(key) && seenAccountKeys.Add(key))
                    {
                        uniqueAccounts.Add(acct);
                    }
                }
                accountsList = uniqueAccounts;

                _logger.LogInformation("Found {Count} unique accounts after dedup", accountsList.Count);

                if (accountsList.Count == 0)
                    return Ok(new { message = "No accounts found." });

                // 2. Get balance for each account
                var accountsWithBalance = new List<object>();
                decimal totalPortfolioValue = 0;
                decimal totalCash = 0;
                decimal totalSecurities = 0;

                foreach (var account in accountsList)
                {
                    string? accountIdKey = null, accountId = null, accountDesc = null, accountType = null, accountMode = null, accountStatus = null;

                    account.TryGetProperty("accountIdKey", out var idKeyElem);
                    account.TryGetProperty("accountId", out var idElem);
                    account.TryGetProperty("accountDesc", out var descElem);
                    account.TryGetProperty("accountType", out var typeElem);
                    account.TryGetProperty("accountMode", out var modeElem);
                    account.TryGetProperty("accountStatus", out var statusElem);

                    accountIdKey = idKeyElem.ValueKind != JsonValueKind.Undefined ? idKeyElem.GetString() : null;
                    accountId = idElem.ValueKind != JsonValueKind.Undefined ? idElem.GetString() : null;
                    accountDesc = descElem.ValueKind != JsonValueKind.Undefined ? descElem.GetString() : null;
                    accountType = typeElem.ValueKind != JsonValueKind.Undefined ? typeElem.GetString() : null;
                    accountMode = modeElem.ValueKind != JsonValueKind.Undefined ? modeElem.GetString() : null;
                    accountStatus = statusElem.ValueKind != JsonValueKind.Undefined ? statusElem.GetString() : null;

                    _logger.LogInformation("Processing account: {Desc} ({Id}) - Status: {Status}", accountDesc, accountId, accountStatus);

                    var keyForBalance = accountIdKey ?? accountId;
                    object? balanceData = null;
                    decimal cash = 0, securities = 0, accountValue = 0;

                    // Skip closed accounts for balance fetching but still include them in the list
                    if (!string.IsNullOrEmpty(keyForBalance) && accountStatus != "CLOSED")
                    {
                        try
                        {
                            var balBaseUrl = $"{apiBase}/{keyForBalance}/balance.json";
                            var balQueryParams = new SortedDictionary<string, string>
                            {
                                { "instType", "BROKERAGE" },
                                { "realTimeNAV", "true" }
                            };
                            var balUrl = balBaseUrl + "?" + string.Join("&", balQueryParams.Select(kvp => $"{kvp.Key}={Uri.EscapeDataString(kvp.Value)}"));

                            var (balAuthHeader, _) = BuildOAuthHeader(consumerKey!, consumerSecret!, _accessToken, _accessTokenSecret, "GET", balBaseUrl, balQueryParams);
                            client.DefaultRequestHeaders.Clear();
                            client.DefaultRequestHeaders.Add("Authorization", balAuthHeader);

                            var balResp = await client.GetAsync(balUrl);
                            var balContent = await balResp.Content.ReadAsStringAsync();

                            _logger.LogInformation("Balance response for {AccountId}: {Content}", keyForBalance, balContent);

                            if (balResp.IsSuccessStatusCode)
                            {
                                var balJson = JsonDocument.Parse(balContent);

                                if (balJson.RootElement.TryGetProperty("BalanceResponse", out var balRespElem))
                                {
                                    // Get account value first from top level
                                    accountValue = TryGetDecimal(balRespElem, "totalAccountValue") 
                                        ?? TryGetDecimal(balRespElem, "accountBalance") 
                                        ?? 0;

                                    // Check Computed section
                                    if (balRespElem.TryGetProperty("Computed", out var computed))
                                    {
                                        // Get account value from RealTimeValues (most accurate)
                                        if (computed.TryGetProperty("RealTimeValues", out var rtv))
                                        {
                                            accountValue = TryGetDecimal(rtv, "totalAccountValue") ?? accountValue;
                                        }

                                        // Try various cash property names - prioritize positive cash values
                                        // cashAvailableForInvestment is typically the "sweep" cash
                                        var cashAvail = TryGetDecimal(computed, "cashAvailableForInvestment");
                                        var cashForWithdraw = TryGetDecimal(computed, "cashAvailableForWithdrawal");
                                        var settledCash = TryGetDecimal(computed, "settledCashForInvestment");
                                        var cashBal = TryGetDecimal(computed, "cashBalance");
                                        var netCash = TryGetDecimal(computed, "netCash");

                                        // Pick the first non-null, non-negative value, or the largest if all negative
                                        var cashValues = new[] { cashAvail, cashForWithdraw, settledCash, cashBal, netCash }
                                            .Where(v => v.HasValue)
                                            .Select(v => v!.Value)
                                            .ToList();

                                        if (cashValues.Any(v => v >= 0))
                                            cash = cashValues.Where(v => v >= 0).Max();
                                        else if (cashValues.Any())
                                            cash = cashValues.Max(); // least negative

                                        _logger.LogInformation("Account {AccountId} cash values - cashAvail: {CashAvail}, cashForWithdraw: {CashForWithdraw}, settledCash: {SettledCash}, cashBal: {CashBal}, netCash: {NetCash}, selected: {Cash}",
                                            keyForBalance, cashAvail, cashForWithdraw, settledCash, cashBal, netCash, cash);
                                    }

                                    // Check Cash section for money market balance
                                    if (balRespElem.TryGetProperty("Cash", out var cashSection))
                                    {
                                        var moneyMkt = TryGetDecimal(cashSection, "moneyMktBalance") ?? 0;
                                        if (moneyMkt > 0 && cash == 0)
                                            cash = moneyMkt;
                                    }
                                }

                                // If we still have 0 for accountValue but have cash, use cash as the value
                                if (accountValue == 0 && cash > 0)
                                {
                                    accountValue = cash;
                                }

                                // Ensure cash is not negative for display purposes
                                // (negative cash could be margin debt which we don't want to show as "cash")
                                if (cash < 0) cash = 0;

                                securities = accountValue - cash;
                                if (securities < 0) securities = 0;

                                _logger.LogInformation("Account {AccountId} final values - cash: {Cash}, securities: {Securities}, total: {Total}",
                                    keyForBalance, cash, securities, accountValue);

                                balanceData = new
                                {
                                    cash,
                                    securities,
                                    totalValue = accountValue
                                };

                                totalCash += cash;
                                totalSecurities += securities;
                                totalPortfolioValue += accountValue;
                            }
                            else
                            {
                                _logger.LogWarning("Failed to get balance for account {AccountId}: {Status} {Content}", keyForBalance, balResp.StatusCode, balContent);
                            }
                        }
                        catch (Exception ex)
                        {
                            _logger.LogWarning(ex, "Error getting balance for account {AccountId}", keyForBalance);
                        }
                    }

                    // If balance API returned 0, try to get value from positions
                    if (balanceData == null || (cash == 0 && accountValue == 0))
                    {
                        try
                        {
                            var positions = await FetchPositionsForAccount(keyForBalance!, consumerKey!, consumerSecret!);
                            var positionsValue = positions.Sum(p => p.MarketValue);
                            if (positionsValue > 0)
                            {
                                _logger.LogInformation("Account {AccountId} using positions value: {Value}", keyForBalance, positionsValue);
                                securities = positionsValue;
                                accountValue = positionsValue + cash;
                                balanceData = new
                                {
                                    cash,
                                    securities,
                                    totalValue = accountValue
                                };
                                totalSecurities += securities;
                                totalPortfolioValue += accountValue;
                            }
                        }
                        catch (Exception ex)
                        {
                            _logger.LogWarning(ex, "Error getting positions for account {AccountId}", keyForBalance);
                        }
                    }

                    // Only add active accounts to the results
                    if (accountStatus != "CLOSED")
                    {
                        accountsWithBalance.Add(new
                        {
                            accountIdKey,
                            accountId,
                            accountDesc,
                            accountType,
                            accountMode,
                            accountStatus,
                            balance = balanceData
                        });
                    }
                }

                return Ok(new
                {
                    accounts = accountsWithBalance,
                    summary = new
                    {
                        totalAccounts = accountsWithBalance.Count,
                        totalCash,
                        totalSecurities,
                        totalPortfolioValue
                    }
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in GetPortfolio");
                return StatusCode(500, new { message = "Internal server error", detail = ex.Message });
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

        [HttpGet("debug-balance/{accountIdKey}")]
        public async Task<IActionResult> DebugBalance(string accountIdKey)
        {
            try
            {
                if (string.IsNullOrEmpty(_accessToken) || string.IsNullOrEmpty(_accessTokenSecret))
                    return Unauthorized(new { message = "Not authenticated" });

                var consumerKey = _config["ETrade:ConsumerKey"];
                var consumerSecret = _config["ETrade:ConsumerSecret"];
                var apiBase = $"{ApiBaseUrl}/v1/accounts";

                using var client = new HttpClient();

                var balBaseUrl = $"{apiBase}/{accountIdKey}/balance.json";
                var balQueryParams = new SortedDictionary<string, string>
                {
                    { "instType", "BROKERAGE" },
                    { "realTimeNAV", "true" }
                };
                var balUrl = balBaseUrl + "?" + string.Join("&", balQueryParams.Select(kvp => $"{kvp.Key}={Uri.EscapeDataString(kvp.Value)}"));

                var (balAuthHeader, _) = BuildOAuthHeader(consumerKey!, consumerSecret!, _accessToken, _accessTokenSecret, "GET", balBaseUrl, balQueryParams);
                client.DefaultRequestHeaders.Clear();
                client.DefaultRequestHeaders.Add("Authorization", balAuthHeader);

                var balResp = await client.GetAsync(balUrl);
                var balContent = await balResp.Content.ReadAsStringAsync();

                if (balResp.IsSuccessStatusCode)
                {
                    var balJson = JsonDocument.Parse(balContent);
                    return Ok(new { raw = balJson.RootElement.ToString(), parsed = JsonSerializer.Deserialize<object>(balContent) });
                }
                return StatusCode((int)balResp.StatusCode, new { error = balContent });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { error = ex.Message });
            }
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

            // Combine OAuth params with query params for signature
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

        [HttpGet("{accountIdKey}/positions")]
        public async Task<IActionResult> GetPositions(string accountIdKey)
        {
            try
            {
                if (string.IsNullOrEmpty(_accessToken) || string.IsNullOrEmpty(_accessTokenSecret))
                    return Unauthorized(new { message = "Not authenticated" });

                var consumerKey = _config["ETrade:ConsumerKey"];
                var consumerSecret = _config["ETrade:ConsumerSecret"];
                var positions = await FetchPositionsForAccount(accountIdKey, consumerKey!, consumerSecret!);

                return Ok(new { accountIdKey, positions });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error fetching positions for account {AccountIdKey}", accountIdKey);
                return StatusCode(500, new { message = "Internal server error", detail = ex.Message });
            }
        }

        [HttpGet("all-positions")]
        public async Task<IActionResult> GetAllPositions()
        {
            try
            {
                if (string.IsNullOrEmpty(_accessToken) || string.IsNullOrEmpty(_accessTokenSecret))
                    return Unauthorized(new { message = "Not authenticated" });

                var consumerKey = _config["ETrade:ConsumerKey"];
                var consumerSecret = _config["ETrade:ConsumerSecret"];
                var apiBase = $"{ApiBaseUrl}/v1/accounts";

                using var client = new HttpClient();

                // Get account list first
                var listUrl = $"{apiBase}/list.json";
                var (listAuthHeader, _) = BuildOAuthHeader(consumerKey!, consumerSecret!, _accessToken, _accessTokenSecret, "GET", listUrl, null);
                client.DefaultRequestHeaders.Clear();
                client.DefaultRequestHeaders.Add("Authorization", listAuthHeader);
                var resp = await client.GetAsync(listUrl);
                var content = await resp.Content.ReadAsStringAsync();

                if (!resp.IsSuccessStatusCode)
                    return StatusCode((int)resp.StatusCode, new { message = "Failed to get accounts" });

                var listJson = JsonDocument.Parse(content);
                var accountKeys = new List<string>();

                if (listJson.RootElement.TryGetProperty("AccountListResponse", out var respElem) &&
                    respElem.TryGetProperty("Accounts", out var acctsElem) &&
                    acctsElem.TryGetProperty("Account", out var acctArrElem) &&
                    acctArrElem.ValueKind == JsonValueKind.Array)
                {
                    foreach (var acct in acctArrElem.EnumerateArray())
                    {
                        if (acct.TryGetProperty("accountIdKey", out var keyElem))
                            accountKeys.Add(keyElem.GetString()!);
                    }
                }

                // Fetch positions for all accounts
                var allPositions = new List<object>();
                var holdingsSummary = new Dictionary<string, HoldingSummary>();
                decimal totalMarketValue = 0;
                decimal totalDayGain = 0;
                decimal totalGain = 0;
                decimal totalCostBasis = 0;

                foreach (var accountKey in accountKeys)
                {
                    var positions = await FetchPositionsForAccount(accountKey, consumerKey!, consumerSecret!);
                    foreach (var pos in positions)
                    {
                        allPositions.Add(new { accountIdKey = accountKey, position = pos });

                        // Aggregate by symbol
                        if (!holdingsSummary.ContainsKey(pos.Symbol))
                        {
                            holdingsSummary[pos.Symbol] = new HoldingSummary
                            {
                                Symbol = pos.Symbol,
                                Description = pos.Description,
                                SecurityType = pos.SecurityType
                            };
                        }

                        var summary = holdingsSummary[pos.Symbol];
                        summary.TotalQuantity += pos.Quantity;
                        summary.TotalMarketValue += pos.MarketValue;
                        summary.TotalCostBasis += pos.CostBasis;
                        summary.TotalDayGain += pos.DayGain;
                        summary.TotalGain += pos.TotalGain;

                        totalMarketValue += pos.MarketValue;
                        totalDayGain += pos.DayGain;
                        totalGain += pos.TotalGain;
                        totalCostBasis += pos.CostBasis;
                    }
                }

                // Calculate percentages
                var aggregatedHoldings = holdingsSummary.Values
                    .Select(h => new
                    {
                        h.Symbol,
                        h.Description,
                        h.SecurityType,
                        h.TotalQuantity,
                        h.TotalMarketValue,
                        h.TotalCostBasis,
                        h.TotalDayGain,
                        h.TotalGain,
                        TotalGainPercent = h.TotalCostBasis > 0 ? (h.TotalGain / h.TotalCostBasis) * 100 : 0,
                        PortfolioPercent = totalMarketValue > 0 ? (h.TotalMarketValue / totalMarketValue) * 100 : 0
                    })
                    .OrderByDescending(h => h.TotalMarketValue)
                    .ToList();

                return Ok(new
                {
                    holdings = aggregatedHoldings,
                    summary = new
                    {
                        totalPositions = aggregatedHoldings.Count,
                        totalMarketValue,
                        totalCostBasis,
                        totalDayGain,
                        totalDayGainPercent = totalCostBasis > 0 ? (totalDayGain / totalMarketValue) * 100 : 0,
                        totalGain,
                        totalGainPercent = totalCostBasis > 0 ? (totalGain / totalCostBasis) * 100 : 0
                    }
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error fetching all positions");
                return StatusCode(500, new { message = "Internal server error", detail = ex.Message });
            }
        }

        /// <summary>
        /// Get comprehensive holdings data with detailed market information
        /// </summary>
        [HttpGet("holdings-detailed")]
        public async Task<IActionResult> GetDetailedHoldings()
        {
            try
            {
                if (string.IsNullOrEmpty(_accessToken) || string.IsNullOrEmpty(_accessTokenSecret))
                    return Unauthorized(new { message = "Not authenticated" });

                var consumerKey = _config["ETrade:ConsumerKey"]!;
                var consumerSecret = _config["ETrade:ConsumerSecret"]!;
                var apiBase = $"{ApiBaseUrl}/v1/accounts";

                using var client = new HttpClient();

                // Get account list
                var listUrl = $"{apiBase}/list.json";
                var (listAuthHeader, _) = BuildOAuthHeader(consumerKey, consumerSecret, _accessToken, _accessTokenSecret, "GET", listUrl, null);
                client.DefaultRequestHeaders.Clear();
                client.DefaultRequestHeaders.Add("Authorization", listAuthHeader);
                var resp = await client.GetAsync(listUrl);
                var content = await resp.Content.ReadAsStringAsync();

                if (!resp.IsSuccessStatusCode)
                    return StatusCode((int)resp.StatusCode, new { message = "Failed to get accounts" });

                var listJson = JsonDocument.Parse(content);
                var accountKeys = new List<(string Key, string Name, string Type)>();

                if (listJson.RootElement.TryGetProperty("AccountListResponse", out var respElem) &&
                    respElem.TryGetProperty("Accounts", out var acctsElem) &&
                    acctsElem.TryGetProperty("Account", out var acctArrElem) &&
                    acctArrElem.ValueKind == JsonValueKind.Array)
                {
                    foreach (var acct in acctArrElem.EnumerateArray())
                    {
                        if (acct.TryGetProperty("accountIdKey", out var keyElem) &&
                            acct.TryGetProperty("accountStatus", out var statusElem) &&
                            statusElem.GetString() == "ACTIVE")
                        {
                            var name = acct.TryGetProperty("accountDesc", out var descElem) ? descElem.GetString() ?? "" : "";
                            var type = acct.TryGetProperty("accountType", out var typeElem) ? typeElem.GetString() ?? "" : "";
                            accountKeys.Add((keyElem.GetString()!, name, type));
                        }
                    }
                }

                // Fetch detailed positions for all accounts
                var allPositions = new List<DetailedPosition>();
                var holdingsSummary = new Dictionary<string, DetailedHoldingSummary>();

                foreach (var (accountKey, accountName, accountType) in accountKeys)
                {
                    var positions = await FetchDetailedPositionsForAccount(accountKey, accountName, accountType, consumerKey, consumerSecret);
                    foreach (var pos in positions)
                    {
                        allPositions.Add(pos);

                        // Aggregate by symbol
                        if (!holdingsSummary.ContainsKey(pos.Symbol))
                        {
                            holdingsSummary[pos.Symbol] = new DetailedHoldingSummary
                            {
                                Symbol = pos.Symbol,
                                Description = pos.Description,
                                SecurityType = pos.SecurityType,
                                AssetClass = pos.AssetClass
                            };
                        }

                        var summary = holdingsSummary[pos.Symbol];
                        summary.TotalQuantity += pos.Quantity;
                        summary.TotalMarketValue += pos.MarketValue;
                        summary.TotalCostBasis += pos.CostBasis;
                        summary.TotalDayGain += pos.DayGain;
                        summary.TotalGain += pos.TotalGain;
                        summary.Lots.Add(pos);

                        // Keep the latest price info
                        if (pos.Price > 0)
                        {
                            summary.CurrentPrice = pos.Price;
                            summary.DayChange = pos.DayChange;
                            summary.DayChangePercent = pos.DayChangePercent;
                            summary.Bid = pos.Bid;
                            summary.Ask = pos.Ask;
                            summary.High52Week = pos.High52Week;
                            summary.Low52Week = pos.Low52Week;
                            summary.Volume = pos.Volume;
                            summary.AverageVolume = pos.AverageVolume;
                            summary.PERatio = pos.PERatio;
                            summary.Dividend = pos.Dividend;
                            summary.DividendYield = pos.DividendYield;
                            summary.ExDividendDate = pos.ExDividendDate;
                            summary.Beta = pos.Beta;
                            summary.MarketCap = pos.MarketCap;
                        }
                    }
                }

                // Calculate totals and percentages
                decimal totalMarketValue = holdingsSummary.Values.Sum(h => h.TotalMarketValue);
                decimal totalCostBasis = holdingsSummary.Values.Sum(h => h.TotalCostBasis);
                decimal totalDayGain = holdingsSummary.Values.Sum(h => h.TotalDayGain);
                decimal totalGain = holdingsSummary.Values.Sum(h => h.TotalGain);

                var detailedHoldings = holdingsSummary.Values
                    .Select(h => new
                    {
                        // Basic info
                        h.Symbol,
                        h.Description,
                        h.SecurityType,
                        h.AssetClass,

                        // Position data
                        quantity = h.TotalQuantity,
                        marketValue = h.TotalMarketValue,
                        costBasis = h.TotalCostBasis,
                        avgCostPerShare = h.TotalQuantity > 0 ? h.TotalCostBasis / h.TotalQuantity : 0,

                        // Gains
                        dayGain = h.TotalDayGain,
                        dayGainPercent = h.TotalMarketValue > 0 && h.TotalMarketValue != h.TotalDayGain 
                            ? (h.TotalDayGain / (h.TotalMarketValue - h.TotalDayGain)) * 100 : 0,
                        totalGain = h.TotalGain,
                        totalGainPercent = h.TotalCostBasis > 0 ? (h.TotalGain / h.TotalCostBasis) * 100 : 0,

                        // Portfolio weight
                        portfolioPercent = totalMarketValue > 0 ? (h.TotalMarketValue / totalMarketValue) * 100 : 0,

                        // Current market data
                        currentPrice = h.CurrentPrice,
                        dayChange = h.DayChange,
                        dayChangePercent = h.DayChangePercent,
                        bid = h.Bid,
                        ask = h.Ask,
                        spread = h.Ask > 0 && h.Bid > 0 ? h.Ask - h.Bid : 0,
                        spreadPercent = h.Bid > 0 && h.Ask > 0 ? ((h.Ask - h.Bid) / h.Bid) * 100 : 0,

                        // 52-week range
                        high52Week = h.High52Week,
                        low52Week = h.Low52Week,
                        range52WeekPercent = h.High52Week > h.Low52Week && h.CurrentPrice > 0
                            ? ((h.CurrentPrice - h.Low52Week) / (h.High52Week - h.Low52Week)) * 100 : 0,

                        // Volume
                        volume = h.Volume,
                        avgVolume = h.AverageVolume,
                        volumeVsAvg = h.AverageVolume > 0 ? ((decimal)h.Volume / h.AverageVolume) * 100 : 0,

                        // Fundamentals
                        peRatio = h.PERatio,
                        dividend = h.Dividend,
                        dividendYield = h.DividendYield,
                        annualDividendIncome = h.Dividend * h.TotalQuantity,
                        exDividendDate = h.ExDividendDate,
                        yieldOnCost = h.TotalCostBasis > 0 && h.Dividend > 0 
                            ? (h.Dividend * h.TotalQuantity / h.TotalCostBasis) * 100 : 0,
                        beta = h.Beta,
                        marketCap = h.MarketCap,

                        // Lot details
                        lotCount = h.Lots.Count,
                        lots = h.Lots.Select(lot => new
                        {
                            accountName = lot.AccountName,
                            accountType = lot.AccountType,
                            quantity = lot.Quantity,
                            costBasis = lot.CostBasis,
                            costPerShare = lot.CostPerShare,
                            marketValue = lot.MarketValue,
                            gain = lot.TotalGain,
                            gainPercent = lot.TotalGainPercent,
                            acquiredDate = lot.AcquiredDate,
                            holdingPeriod = lot.HoldingPeriod,
                            termType = lot.TermType // Short-term vs Long-term
                        }).ToList()
                    })
                    .OrderByDescending(h => h.marketValue)
                    .ToList();

                // Calculate summary statistics
                var winningPositions = detailedHoldings.Count(h => h.totalGain > 0);
                var losingPositions = detailedHoldings.Count(h => h.totalGain < 0);
                var breakEvenPositions = detailedHoldings.Count(h => h.totalGain == 0);

                var bestPerformer = detailedHoldings.OrderByDescending(h => h.totalGainPercent).FirstOrDefault();
                var worstPerformer = detailedHoldings.OrderBy(h => h.totalGainPercent).FirstOrDefault();
                var largestPosition = detailedHoldings.OrderByDescending(h => h.marketValue).FirstOrDefault();

                // Calculate diversification metrics
                var byAssetClass = detailedHoldings
                    .GroupBy(h => h.AssetClass)
                    .Select(g => new
                    {
                        assetClass = g.Key,
                        value = g.Sum(h => h.marketValue),
                        percent = totalMarketValue > 0 ? (g.Sum(h => h.marketValue) / totalMarketValue) * 100 : 0,
                        count = g.Count()
                    })
                    .OrderByDescending(g => g.value)
                    .ToList();

                // Calculate income metrics
                var totalAnnualDividends = detailedHoldings.Sum(h => h.annualDividendIncome);
                var portfolioYield = totalMarketValue > 0 ? (totalAnnualDividends / totalMarketValue) * 100 : 0;
                var yieldOnCost = totalCostBasis > 0 ? (totalAnnualDividends / totalCostBasis) * 100 : 0;

                return Ok(new
                {
                    holdings = detailedHoldings,
                    summary = new
                    {
                        totalPositions = detailedHoldings.Count,
                        totalMarketValue,
                        totalCostBasis,
                        totalDayGain,
                        totalDayGainPercent = totalMarketValue > 0 && totalMarketValue != totalDayGain 
                            ? (totalDayGain / (totalMarketValue - totalDayGain)) * 100 : 0,
                        totalGain,
                        totalGainPercent = totalCostBasis > 0 ? (totalGain / totalCostBasis) * 100 : 0,
                        winningPositions,
                        losingPositions,
                        breakEvenPositions,
                        winRate = detailedHoldings.Count > 0 
                            ? (decimal)winningPositions / detailedHoldings.Count * 100 : 0
                    },
                    performance = new
                    {
                        bestPerformer = bestPerformer != null ? new { bestPerformer.Symbol, bestPerformer.totalGainPercent, bestPerformer.totalGain } : null,
                        worstPerformer = worstPerformer != null ? new { worstPerformer.Symbol, worstPerformer.totalGainPercent, worstPerformer.totalGain } : null,
                        largestPosition = largestPosition != null ? new { largestPosition.Symbol, largestPosition.marketValue, largestPosition.portfolioPercent } : null
                    },
                    diversification = new
                    {
                        byAssetClass,
                        concentrationTop5 = detailedHoldings.Take(5).Sum(h => h.portfolioPercent),
                        effectivePositions = detailedHoldings.Count > 0 
                            ? 1 / detailedHoldings.Sum(h => Math.Pow((double)h.portfolioPercent / 100, 2)) : 0
                    },
                    income = new
                    {
                        totalAnnualDividends,
                        portfolioYield,
                        yieldOnCost,
                        monthlyIncome = totalAnnualDividends / 12
                    }
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error fetching detailed holdings");
                return StatusCode(500, new { message = "Internal server error", detail = ex.Message });
            }
        }

        private async Task<List<DetailedPosition>> FetchDetailedPositionsForAccount(
            string accountIdKey, string accountName, string accountType, 
            string consumerKey, string consumerSecret)
        {
            var positions = new List<DetailedPosition>();
            var apiBase = $"{ApiBaseUrl}/v1/accounts";

            using var client = new HttpClient();

            var portfolioBaseUrl = $"{apiBase}/{accountIdKey}/portfolio.json";
            var (authHeader, _) = BuildOAuthHeader(consumerKey, consumerSecret, _accessToken!, _accessTokenSecret!, "GET", portfolioBaseUrl, null);

            client.DefaultRequestHeaders.Clear();
            client.DefaultRequestHeaders.Add("Authorization", authHeader);

            var resp = await client.GetAsync(portfolioBaseUrl);
            var content = await resp.Content.ReadAsStringAsync();

            if (!resp.IsSuccessStatusCode || string.IsNullOrWhiteSpace(content))
                return positions;

            try
            {
                var json = JsonDocument.Parse(content);

                if (json.RootElement.TryGetProperty("PortfolioResponse", out var portfolioResp) &&
                    portfolioResp.TryGetProperty("AccountPortfolio", out var accountPortfolios) &&
                    accountPortfolios.ValueKind == JsonValueKind.Array)
                {
                    foreach (var accountPortfolio in accountPortfolios.EnumerateArray())
                    {
                        if (accountPortfolio.TryGetProperty("Position", out var positionsArray) &&
                            positionsArray.ValueKind == JsonValueKind.Array)
                        {
                            foreach (var pos in positionsArray.EnumerateArray())
                            {
                                var position = ParseDetailedPosition(pos, accountName, accountType, accountIdKey);
                                if (position != null)
                                    positions.Add(position);
                            }
                        }
                    }
                }

                // Get additional quote data for all symbols
                var symbols = positions.Select(p => p.Symbol).Distinct().ToList();
                if (symbols.Count > 0)
                {
                    var quoteData = await FetchQuotesForSymbols(symbols, consumerKey, consumerSecret);
                    foreach (var pos in positions)
                    {
                        if (quoteData.TryGetValue(pos.Symbol, out var quote))
                        {
                            pos.Bid = quote.Bid;
                            pos.Ask = quote.Ask;
                            pos.High52Week = quote.High52Week;
                            pos.Low52Week = quote.Low52Week;
                            pos.Volume = quote.Volume;
                            pos.AverageVolume = quote.AverageVolume;
                            pos.PERatio = quote.PERatio;
                            pos.Dividend = quote.Dividend;
                            pos.DividendYield = quote.DividendYield;
                            pos.ExDividendDate = quote.ExDividendDate;
                            pos.Beta = quote.Beta;
                            pos.MarketCap = quote.MarketCap;
                        }
                    }
                }
            }
            catch (JsonException ex)
            {
                _logger.LogWarning(ex, "Failed to parse detailed portfolio for account {AccountIdKey}", accountIdKey);
            }

            return positions;
        }

        private DetailedPosition? ParseDetailedPosition(JsonElement pos, string accountName, string accountType, string accountIdKey)
        {
            try
            {
                var position = new DetailedPosition
                {
                    AccountIdKey = accountIdKey,
                    AccountName = accountName,
                    AccountType = accountType
                };

                // Get product info
                if (pos.TryGetProperty("Product", out var product))
                {
                    if (product.TryGetProperty("symbol", out var symElem))
                        position.Symbol = symElem.GetString() ?? "";
                    if (product.TryGetProperty("securityType", out var secTypeElem))
                        position.SecurityType = secTypeElem.GetString() ?? "";
                }

                if (pos.TryGetProperty("symbolDescription", out var descElem))
                    position.Description = descElem.GetString() ?? "";

                // Quantity and dates
                if (pos.TryGetProperty("quantity", out var qtyElem))
                    position.Quantity = qtyElem.GetDecimal();

                if (pos.TryGetProperty("dateAcquired", out var dateElem))
                {
                    var dateVal = dateElem.GetInt64();
                    position.AcquiredDate = DateTimeOffset.FromUnixTimeMilliseconds(dateVal).DateTime;
                    position.HoldingPeriod = (DateTime.Now - position.AcquiredDate.Value).Days;
                    position.TermType = position.HoldingPeriod >= 365 ? "Long-term" : "Short-term";
                }

                // Price and values
                if (pos.TryGetProperty("Quick", out var quick))
                {
                    if (quick.TryGetProperty("lastTrade", out var lastTradeElem))
                        position.Price = lastTradeElem.GetDecimal();
                    if (quick.TryGetProperty("change", out var changeElem))
                    {
                        position.DayChange = changeElem.GetDecimal();
                        position.DayGain = position.DayChange * position.Quantity;
                    }
                    if (quick.TryGetProperty("changePct", out var changePctElem))
                        position.DayChangePercent = changePctElem.GetDecimal();
                }

                if (pos.TryGetProperty("marketValue", out var mvElem))
                    position.MarketValue = mvElem.GetDecimal();

                if (pos.TryGetProperty("totalCost", out var costElem))
                    position.CostBasis = costElem.GetDecimal();
                else if (pos.TryGetProperty("costPerShare", out var cpsElem))
                {
                    position.CostPerShare = cpsElem.GetDecimal();
                    position.CostBasis = position.CostPerShare * position.Quantity;
                }

                if (position.CostBasis > 0 && position.Quantity > 0)
                    position.CostPerShare = position.CostBasis / position.Quantity;

                if (pos.TryGetProperty("totalGain", out var tgElem))
                    position.TotalGain = tgElem.GetDecimal();
                else
                    position.TotalGain = position.MarketValue - position.CostBasis;

                if (pos.TryGetProperty("totalGainPct", out var tgpElem))
                    position.TotalGainPercent = tgpElem.GetDecimal();
                else if (position.CostBasis > 0)
                    position.TotalGainPercent = (position.TotalGain / position.CostBasis) * 100;

                if (pos.TryGetProperty("daysGain", out var dgElem))
                    position.DayGain = dgElem.GetDecimal();

                if (pos.TryGetProperty("daysGainPct", out var dgpElem))
                    position.DayChangePercent = dgpElem.GetDecimal();

                // Set asset class
                position.AssetClass = FullPort.Models.AssetClassMappings.GetAssetClass(position.Symbol).ToString();

                return string.IsNullOrEmpty(position.Symbol) ? null : position;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error parsing detailed position");
                return null;
            }
        }

        private async Task<Dictionary<string, QuoteInfo>> FetchQuotesForSymbols(List<string> symbols, string consumerKey, string consumerSecret)
        {
            var quotes = new Dictionary<string, QuoteInfo>();

            // E*TRADE allows up to 25 symbols per request
            var batches = symbols.Chunk(25);

            foreach (var batch in batches)
            {
                try
                {
                    var symbolsParam = string.Join(",", batch);
                    var quoteUrl = $"{ApiBaseUrl}/v1/market/quote/{symbolsParam}.json";

                    using var client = new HttpClient();
                    var (authHeader, _) = BuildOAuthHeader(consumerKey, consumerSecret, _accessToken!, _accessTokenSecret!, "GET", quoteUrl, null);
                    client.DefaultRequestHeaders.Add("Authorization", authHeader);

                    var response = await client.GetAsync(quoteUrl);
                    var content = await response.Content.ReadAsStringAsync();

                    if (response.IsSuccessStatusCode && !string.IsNullOrWhiteSpace(content))
                    {
                        var doc = JsonDocument.Parse(content);
                        if (doc.RootElement.TryGetProperty("QuoteResponse", out var quoteResp) &&
                            quoteResp.TryGetProperty("QuoteData", out var quoteData))
                        {
                            foreach (var quote in quoteData.EnumerateArray())
                            {
                                var quoteInfo = ParseQuoteInfo(quote);
                                if (quoteInfo != null)
                                    quotes[quoteInfo.Symbol] = quoteInfo;
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to fetch quotes for batch");
                }
            }

            return quotes;
        }

        private QuoteInfo? ParseQuoteInfo(JsonElement quote)
        {
            try
            {
                var info = new QuoteInfo();

                if (quote.TryGetProperty("Product", out var product) &&
                    product.TryGetProperty("symbol", out var symbol))
                {
                    info.Symbol = symbol.GetString() ?? "";
                }

                if (quote.TryGetProperty("All", out var all))
                {
                    info.Bid = TryGetDecimal(all, "bid") ?? 0;
                    info.Ask = TryGetDecimal(all, "ask") ?? 0;
                    info.High52Week = TryGetDecimal(all, "high52") ?? 0;
                    info.Low52Week = TryGetDecimal(all, "low52") ?? 0;
                    info.Volume = (long)(TryGetDecimal(all, "totalVolume") ?? 0);
                    info.AverageVolume = (long)(TryGetDecimal(all, "averageVolume") ?? 0);
                    info.PERatio = TryGetDecimal(all, "pe") ?? 0;
                    info.Dividend = TryGetDecimal(all, "dividend") ?? 0;
                    info.DividendYield = TryGetDecimal(all, "dividendYield") ?? 0;
                    info.Beta = TryGetDecimal(all, "beta") ?? 0;
                    info.MarketCap = TryGetDecimal(all, "marketCap") ?? 0;

                    if (all.TryGetProperty("exDividendDate", out var exDiv))
                    {
                        var dateVal = exDiv.GetInt64();
                        if (dateVal > 0)
                            info.ExDividendDate = DateTimeOffset.FromUnixTimeMilliseconds(dateVal).DateTime;
                    }
                }

                return string.IsNullOrEmpty(info.Symbol) ? null : info;
            }
            catch
            {
                return null;
            }
        }

        private class DetailedPosition
        {
            public string AccountIdKey { get; set; } = "";
            public string AccountName { get; set; } = "";
            public string AccountType { get; set; } = "";
            public string Symbol { get; set; } = "";
            public string Description { get; set; } = "";
            public string SecurityType { get; set; } = "";
            public string AssetClass { get; set; } = "";
            public decimal Quantity { get; set; }
            public decimal Price { get; set; }
            public decimal MarketValue { get; set; }
            public decimal CostBasis { get; set; }
            public decimal CostPerShare { get; set; }
            public decimal DayChange { get; set; }
            public decimal DayChangePercent { get; set; }
            public decimal DayGain { get; set; }
            public decimal TotalGain { get; set; }
            public decimal TotalGainPercent { get; set; }
            public DateTime? AcquiredDate { get; set; }
            public int HoldingPeriod { get; set; }
            public string TermType { get; set; } = "";

            // Market data
            public decimal Bid { get; set; }
            public decimal Ask { get; set; }
            public decimal High52Week { get; set; }
            public decimal Low52Week { get; set; }
            public long Volume { get; set; }
            public long AverageVolume { get; set; }
            public decimal PERatio { get; set; }
            public decimal Dividend { get; set; }
            public decimal DividendYield { get; set; }
            public DateTime? ExDividendDate { get; set; }
            public decimal Beta { get; set; }
            public decimal MarketCap { get; set; }
        }

        private class DetailedHoldingSummary
        {
            public string Symbol { get; set; } = "";
            public string Description { get; set; } = "";
            public string SecurityType { get; set; } = "";
            public string AssetClass { get; set; } = "";
            public decimal TotalQuantity { get; set; }
            public decimal TotalMarketValue { get; set; }
            public decimal TotalCostBasis { get; set; }
            public decimal TotalDayGain { get; set; }
            public decimal TotalGain { get; set; }
            public decimal CurrentPrice { get; set; }
            public decimal DayChange { get; set; }
            public decimal DayChangePercent { get; set; }
            public decimal Bid { get; set; }
            public decimal Ask { get; set; }
            public decimal High52Week { get; set; }
            public decimal Low52Week { get; set; }
            public long Volume { get; set; }
            public long AverageVolume { get; set; }
            public decimal PERatio { get; set; }
            public decimal Dividend { get; set; }
            public decimal DividendYield { get; set; }
            public DateTime? ExDividendDate { get; set; }
            public decimal Beta { get; set; }
            public decimal MarketCap { get; set; }
            public List<DetailedPosition> Lots { get; set; } = [];
        }

        private class QuoteInfo
        {
            public string Symbol { get; set; } = "";
            public decimal Bid { get; set; }
            public decimal Ask { get; set; }
            public decimal High52Week { get; set; }
            public decimal Low52Week { get; set; }
            public long Volume { get; set; }
            public long AverageVolume { get; set; }
            public decimal PERatio { get; set; }
            public decimal Dividend { get; set; }
            public decimal DividendYield { get; set; }
            public DateTime? ExDividendDate { get; set; }
            public decimal Beta { get; set; }
            public decimal MarketCap { get; set; }
        }

        private async Task<List<PositionData>> FetchPositionsForAccount(string accountIdKey, string consumerKey, string consumerSecret)
        {
            var positions = new List<PositionData>();
            var apiBase = $"{ApiBaseUrl}/v1/accounts";

            using var client = new HttpClient();

            var portfolioBaseUrl = $"{apiBase}/{accountIdKey}/portfolio.json";
            var (authHeader, _) = BuildOAuthHeader(consumerKey, consumerSecret, _accessToken!, _accessTokenSecret!, "GET", portfolioBaseUrl, null);

            client.DefaultRequestHeaders.Clear();
            client.DefaultRequestHeaders.Add("Authorization", authHeader);

            var resp = await client.GetAsync(portfolioBaseUrl);
            var content = await resp.Content.ReadAsStringAsync();

            if (!resp.IsSuccessStatusCode)
            {
                _logger.LogWarning("Failed to get portfolio for account {AccountIdKey}: {Status} {Content}", accountIdKey, resp.StatusCode, content);
                return positions;
            }

            // Handle empty response (account with no positions)
            if (string.IsNullOrWhiteSpace(content))
            {
                _logger.LogInformation("Account {AccountIdKey} has no positions (empty response)", accountIdKey);
                return positions;
            }

            try
            {
                var json = JsonDocument.Parse(content);

                // E*TRADE response structure: PortfolioResponse.AccountPortfolio[].Position[]
                if (json.RootElement.TryGetProperty("PortfolioResponse", out var portfolioResp) &&
                    portfolioResp.TryGetProperty("AccountPortfolio", out var accountPortfolios) &&
                    accountPortfolios.ValueKind == JsonValueKind.Array)
                {
                    foreach (var accountPortfolio in accountPortfolios.EnumerateArray())
                    {
                        if (accountPortfolio.TryGetProperty("Position", out var positionsArray) &&
                            positionsArray.ValueKind == JsonValueKind.Array)
                        {
                            foreach (var pos in positionsArray.EnumerateArray())
                            {
                                var position = ParsePosition(pos);
                                if (position != null)
                                    positions.Add(position);
                            }
                        }
                    }
                }
            }
            catch (JsonException ex)
            {
                _logger.LogWarning(ex, "Failed to parse portfolio response for account {AccountIdKey}: {Content}", accountIdKey, content);
            }

            return positions;
        }

        private PositionData? ParsePosition(JsonElement pos)
        {
            try
            {
                var symbol = "";
                var description = "";
                var securityType = "";
                decimal quantity = 0, price = 0, marketValue = 0, costBasis = 0;
                decimal dayGain = 0, dayGainPercent = 0, totalGain = 0, totalGainPercent = 0;

                // Get product info
                if (pos.TryGetProperty("Product", out var product))
                {
                    if (product.TryGetProperty("symbol", out var symElem))
                        symbol = symElem.GetString() ?? "";
                    if (product.TryGetProperty("securityType", out var secTypeElem))
                        securityType = secTypeElem.GetString() ?? "";
                }

                if (pos.TryGetProperty("symbolDescription", out var descElem))
                    description = descElem.GetString() ?? "";

                // Quantity
                if (pos.TryGetProperty("quantity", out var qtyElem))
                    quantity = qtyElem.GetDecimal();

                // Price and values
                if (pos.TryGetProperty("Quick", out var quick))
                {
                    if (quick.TryGetProperty("lastTrade", out var lastTradeElem))
                        price = lastTradeElem.GetDecimal();
                    if (quick.TryGetProperty("change", out var changeElem))
                        dayGain = changeElem.GetDecimal() * quantity;
                    if (quick.TryGetProperty("changePct", out var changePctElem))
                        dayGainPercent = changePctElem.GetDecimal();
                }

                if (pos.TryGetProperty("marketValue", out var mvElem))
                    marketValue = mvElem.GetDecimal();

                if (pos.TryGetProperty("totalCost", out var costElem))
                    costBasis = costElem.GetDecimal();
                else if (pos.TryGetProperty("costPerShare", out var cpsElem))
                    costBasis = cpsElem.GetDecimal() * quantity;

                if (pos.TryGetProperty("totalGain", out var tgElem))
                    totalGain = tgElem.GetDecimal();
                else
                    totalGain = marketValue - costBasis;

                if (pos.TryGetProperty("totalGainPct", out var tgpElem))
                    totalGainPercent = tgpElem.GetDecimal();
                else if (costBasis > 0)
                    totalGainPercent = (totalGain / costBasis) * 100;

                // Get day gain from daysGain if available
                if (pos.TryGetProperty("daysGain", out var dgElem))
                    dayGain = dgElem.GetDecimal();

                if (pos.TryGetProperty("daysGainPct", out var dgpElem))
                    dayGainPercent = dgpElem.GetDecimal();

                return new PositionData
                {
                    Symbol = symbol,
                    Description = description,
                    SecurityType = securityType,
                    Quantity = quantity,
                    Price = price,
                    MarketValue = marketValue,
                    CostBasis = costBasis,
                    DayGain = dayGain,
                    DayGainPercent = dayGainPercent,
                    TotalGain = totalGain,
                    TotalGainPercent = totalGainPercent
                };
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error parsing position");
                return null;
            }
        }

        private class PositionData
        {
            public string Symbol { get; set; } = "";
            public string Description { get; set; } = "";
            public string SecurityType { get; set; } = "";
            public decimal Quantity { get; set; }
            public decimal Price { get; set; }
            public decimal MarketValue { get; set; }
            public decimal CostBasis { get; set; }
            public decimal DayGain { get; set; }
            public decimal DayGainPercent { get; set; }
            public decimal TotalGain { get; set; }
            public decimal TotalGainPercent { get; set; }
        }

        private class HoldingSummary
        {
            public string Symbol { get; set; } = "";
            public string Description { get; set; } = "";
            public string SecurityType { get; set; } = "";
            public decimal TotalQuantity { get; set; }
            public decimal TotalMarketValue { get; set; }
            public decimal TotalCostBasis { get; set; }
            public decimal TotalDayGain { get; set; }
            public decimal TotalGain { get; set; }
        }

        [HttpGet("authenticate")]
        public async Task<IActionResult> Authenticate()
        {
            var consumerKey = _config["ETrade:ConsumerKey"];
            var consumerSecret = _config["ETrade:ConsumerSecret"];
            var callbackUrl = "oob"; // OOB flow
            var requestTokenUrl = $"{ApiBaseUrl}/oauth/request_token";

            var oauthNonce = Guid.NewGuid().ToString("N");
            var oauthTimestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString();
            var parameters = new SortedDictionary<string, string>
            {
                { "oauth_callback", callbackUrl },
                { "oauth_consumer_key", consumerKey },
                { "oauth_nonce", oauthNonce },
                { "oauth_signature_method", "HMAC-SHA1" },
                { "oauth_timestamp", oauthTimestamp },
                { "oauth_version", "1.0" }
            };
            var baseString = $"POST&{Uri.EscapeDataString(requestTokenUrl)}&" + Uri.EscapeDataString(string.Join("&", parameters.Select(kvp => $"{Uri.EscapeDataString(kvp.Key)}={Uri.EscapeDataString(kvp.Value)}")));
            var signingKey = $"{Uri.EscapeDataString(consumerSecret)}&";
            using var hasher = new HMACSHA1(Encoding.ASCII.GetBytes(signingKey));
            var signature = Convert.ToBase64String(hasher.ComputeHash(Encoding.ASCII.GetBytes(baseString)));
            var authHeader = $"OAuth oauth_callback=\"{Uri.EscapeDataString(callbackUrl)}\", oauth_consumer_key=\"{consumerKey}\", oauth_nonce=\"{oauthNonce}\", oauth_signature=\"{Uri.EscapeDataString(signature)}\", oauth_signature_method=\"HMAC-SHA1\", oauth_timestamp=\"{oauthTimestamp}\", oauth_version=\"1.0\"";

            using var client = new HttpClient();
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("OAuth", authHeader.Substring(6));
            var resp = await client.PostAsync(requestTokenUrl, null);
            var content = await resp.Content.ReadAsStringAsync();
            if (!resp.IsSuccessStatusCode)
                return StatusCode((int)resp.StatusCode, content);
            var query = System.Web.HttpUtility.ParseQueryString(content);
            var oauthToken = query["oauth_token"];
            var oauthTokenSecret = query["oauth_token_secret"];
            if (oauthToken == null || oauthTokenSecret == null)
                return StatusCode(500, "Failed to get request token");
            _requestTokens[oauthToken] = oauthTokenSecret;
            var authorizeUrl = $"https://us.etrade.com/e/t/etws/authorize?key={consumerKey}&token={oauthToken}";
            // Instead of redirect, return the URL and token to the frontend
            return Ok(new { authorizeUrl, oauthToken });
        }

        [HttpPost("sign-out")]
        public IActionResult SignOut()
        {
            _accessToken = null;
            _accessTokenSecret = null;
            _requestTokens.Clear();
            _logger.LogInformation("User signed out, tokens cleared");
            return Ok(new { success = true });
        }

        [HttpPost("access-token")]
        public async Task<IActionResult> ExchangePin([FromBody] OobPinRequest request)
        {
            var consumerKey = _config["ETrade:ConsumerKey"];
            var consumerSecret = _config["ETrade:ConsumerSecret"];
            if (!_requestTokens.TryGetValue(request.OAuthToken, out var oauthTokenSecret))
                return StatusCode(400, "Invalid or expired request token");
            var accessTokenUrl = $"{ApiBaseUrl}/oauth/access_token";
            var oauthNonce = Guid.NewGuid().ToString("N");
            var oauthTimestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString();

            // Build OAuth parameters
            var oauthParams = new SortedDictionary<string, string>
            {
                { "oauth_consumer_key", consumerKey },
                { "oauth_nonce", oauthNonce },
                { "oauth_signature_method", "HMAC-SHA1" },
                { "oauth_timestamp", oauthTimestamp },
                { "oauth_token", request.OAuthToken },
                { "oauth_verifier", request.Pin },
                { "oauth_version", "1.0" }
            };

            // Build signature base string
            var baseString = "POST&" + Uri.EscapeDataString(accessTokenUrl) + "&" +
                Uri.EscapeDataString(string.Join("&", oauthParams.Select(kvp => $"{Uri.EscapeDataString(kvp.Key)}={Uri.EscapeDataString(kvp.Value)}")));
            var signingKey = $"{Uri.EscapeDataString(consumerSecret)}&{Uri.EscapeDataString(oauthTokenSecret)}";
            using var hasher = new HMACSHA1(Encoding.ASCII.GetBytes(signingKey));
            var signature = Convert.ToBase64String(hasher.ComputeHash(Encoding.ASCII.GetBytes(baseString)));

            // Build the Authorization header
            var headerParams = new List<string>();
            foreach (var kvp in oauthParams)
                headerParams.Add($"{kvp.Key}=\"{Uri.EscapeDataString(kvp.Value)}\"");
            headerParams.Add($"oauth_signature=\"{Uri.EscapeDataString(signature)}\"");
            var authHeader = "OAuth " + string.Join(", ", headerParams);

            using var client = new HttpClient();
            client.DefaultRequestHeaders.Clear();
            client.DefaultRequestHeaders.Add("Authorization", authHeader);

            var resp = await client.PostAsync(accessTokenUrl, null); // No body
            var content = await resp.Content.ReadAsStringAsync();
            if (!resp.IsSuccessStatusCode)
                return StatusCode((int)resp.StatusCode, content);
            var query = System.Web.HttpUtility.ParseQueryString(content);
            _accessToken = query["oauth_token"];
            _accessTokenSecret = query["oauth_token_secret"];
            return Ok(new { success = true });
        }

        public class OobPinRequest
        {
            public string OAuthToken { get; set; } = string.Empty;
            public string Pin { get; set; } = string.Empty;
        }
    }
}
