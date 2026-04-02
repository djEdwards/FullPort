using Microsoft.AspNetCore.Mvc;
using System.Text.Json;
using System.Text;
using System.Security.Cryptography;
using FullPort.Models;

namespace FullPort.Controllers;

[ApiController]
[Route("api/[controller]")]
public class RebalanceController : ControllerBase
{
    private readonly IConfiguration _config;
    private readonly ILogger<RebalanceController> _logger;
    private readonly IWebHostEnvironment _env;

    // Shared token storage (in production, use a proper session/token store)
    private static string? AccessToken => PortfolioController.GetAccessToken();
    private static string? AccessTokenSecret => PortfolioController.GetAccessTokenSecret();

    public RebalanceController(IConfiguration config, ILogger<RebalanceController> logger, IWebHostEnvironment env)
    {
        _config = config;
        _logger = logger;
        _env = env;
    }

    private string ApiBaseUrl => _config.GetValue<bool>("ETrade:UseSandbox")
        ? "https://apisb.etrade.com"
        : "https://api.etrade.com";

    /// <summary>
    /// Get predefined rebalancing models
    /// </summary>
    [HttpGet("models")]
    public IActionResult GetModels()
    {
        var models = new List<RebalanceModel>
        {
            new()
            {
                Name = "Ultra-Conservative",
                Description = "Capital preservation: 10% stocks, 60% bonds, 20% cash",
                RiskProfile = RiskProfile.UltraConservative,
                RebalanceByAssetClass = true,
                Allocations = AssetClassMappings.RiskProfileAllocations[RiskProfile.UltraConservative]
                    .Select(kvp => new TargetAllocation { AssetClass = kvp.Key, TargetPercent = kvp.Value })
                    .ToList()
            },
            new()
            {
                Name = "Conservative",
                Description = "Low risk: 25% stocks, 50% bonds, 10% real estate",
                RiskProfile = RiskProfile.Conservative,
                RebalanceByAssetClass = true,
                Allocations = AssetClassMappings.RiskProfileAllocations[RiskProfile.Conservative]
                    .Select(kvp => new TargetAllocation { AssetClass = kvp.Key, TargetPercent = kvp.Value })
                    .ToList()
            },
            new()
            {
                Name = "Income",
                Description = "Yield-focused: dividends, bonds, REITs for income",
                RiskProfile = RiskProfile.Income,
                RebalanceByAssetClass = true,
                Allocations = AssetClassMappings.RiskProfileAllocations[RiskProfile.Income]
                    .Select(kvp => new TargetAllocation { AssetClass = kvp.Key, TargetPercent = kvp.Value })
                    .ToList()
            },
            new()
            {
                Name = "Balanced",
                Description = "Classic 60/40: 60% stocks, 30% bonds, 10% alternatives",
                RiskProfile = RiskProfile.Balanced,
                RebalanceByAssetClass = true,
                Allocations = AssetClassMappings.RiskProfileAllocations[RiskProfile.Balanced]
                    .Select(kvp => new TargetAllocation { AssetClass = kvp.Key, TargetPercent = kvp.Value })
                    .ToList()
            },
            new()
            {
                Name = "Moderate",
                Description = "Balanced growth: 50% stocks, 25% bonds, alternatives + crypto",
                RiskProfile = RiskProfile.Moderate,
                RebalanceByAssetClass = true,
                Allocations = AssetClassMappings.RiskProfileAllocations[RiskProfile.Moderate]
                    .Select(kvp => new TargetAllocation { AssetClass = kvp.Key, TargetPercent = kvp.Value })
                    .ToList()
            },
            new()
            {
                Name = "Dividend Growth",
                Description = "Dividend aristocrats: quality dividend stocks + REITs",
                RiskProfile = RiskProfile.DividendGrowth,
                RebalanceByAssetClass = true,
                Allocations = AssetClassMappings.RiskProfileAllocations[RiskProfile.DividendGrowth]
                    .Select(kvp => new TargetAllocation { AssetClass = kvp.Key, TargetPercent = kvp.Value })
                    .ToList()
            },
            new()
            {
                Name = "Growth",
                Description = "Capital appreciation: 70% stocks, growth focus + crypto",
                RiskProfile = RiskProfile.Growth,
                RebalanceByAssetClass = true,
                Allocations = AssetClassMappings.RiskProfileAllocations[RiskProfile.Growth]
                    .Select(kvp => new TargetAllocation { AssetClass = kvp.Key, TargetPercent = kvp.Value })
                    .ToList()
            },
            new()
            {
                Name = "Aggressive",
                Description = "High risk: 65% stocks, 10% crypto, minimal bonds",
                RiskProfile = RiskProfile.Aggressive,
                RebalanceByAssetClass = true,
                Allocations = AssetClassMappings.RiskProfileAllocations[RiskProfile.Aggressive]
                    .Select(kvp => new TargetAllocation { AssetClass = kvp.Key, TargetPercent = kvp.Value })
                    .ToList()
            },
            new()
            {
                Name = "Ultra-Aggressive",
                Description = "Maximum growth: 60% stocks, 25% crypto, emerging markets",
                RiskProfile = RiskProfile.UltraAggressive,
                RebalanceByAssetClass = true,
                Allocations = AssetClassMappings.RiskProfileAllocations[RiskProfile.UltraAggressive]
                    .Select(kvp => new TargetAllocation { AssetClass = kvp.Key, TargetPercent = kvp.Value })
                    .ToList()
            }
        };

        return Ok(models);
    }

    /// <summary>
    /// Analyze current portfolio allocation
    /// </summary>
    /// <param name="accountIdKey">Optional account ID to analyze</param>
    /// <param name="testCash">Override cash balance for testing (Development mode only)</param>
    [HttpGet("analyze")]
    public async Task<IActionResult> AnalyzePortfolio([FromQuery] string? accountIdKey = null, [FromQuery] decimal? testCash = null)
    {
        if (string.IsNullOrEmpty(AccessToken) || string.IsNullOrEmpty(AccessTokenSecret))
            return Unauthorized(new { message = "Not authenticated. Please sign in via the Dashboard tab." });

        try
        {
            _logger.LogInformation("Analyzing portfolio...");
            var holdings = await GetAllHoldings(accountIdKey);
            var actualCash = await GetCashBalance(accountIdKey);
            var accountIds = await GetAccountIds(accountIdKey);

            // Allow test cash override in Development mode only
            var cash = actualCash;
            var isTestMode = false;
            if (testCash.HasValue && testCash.Value > 0 && _env.IsDevelopment())
            {
                cash = testCash.Value;
                isTestMode = true;
                _logger.LogWarning("⚠️ TEST MODE: Using simulated cash balance of {TestCash} (actual: {ActualCash})", testCash.Value, actualCash);
            }

            _logger.LogInformation("Found {Count} holdings, cash: {Cash}, accounts: {Accounts}", 
                holdings.Count, cash, string.Join(",", accountIds));

            var totalValue = holdings.Sum(h => h.MarketValue) + cash;

            // Group by asset class
            var byAssetClass = holdings
                .GroupBy(h => AssetClassMappings.GetAssetClass(h.Symbol))
                .Select(g => new TargetAllocation
                {
                    AssetClass = g.Key,
                    CurrentValue = g.Sum(h => h.MarketValue),
                    CurrentPercent = totalValue > 0 ? (g.Sum(h => h.MarketValue) / totalValue) * 100 : 0
                })
                .ToList();

            // Add cash
            byAssetClass.Add(new TargetAllocation
            {
                AssetClass = AssetClass.Cash,
                CurrentValue = cash,
                CurrentPercent = totalValue > 0 ? (cash / totalValue) * 100 : 0
            });

            // Group by symbol
            var bySymbol = holdings
                .Select(h => new TargetAllocation
                {
                    Symbol = h.Symbol,
                    AssetClass = AssetClassMappings.GetAssetClass(h.Symbol),
                    CurrentValue = h.MarketValue,
                    CurrentPercent = totalValue > 0 ? (h.MarketValue / totalValue) * 100 : 0
                })
                .OrderByDescending(a => a.CurrentValue)
                .ToList();

            return Ok(new
            {
                totalValue,
                cash,
                actualCash,           // Always return actual cash for reference
                isTestMode,           // Flag to show UI warning
                invested = totalValue - cash,
                byAssetClass = byAssetClass.OrderByDescending(a => a.CurrentValue),
                bySymbol,
                holdingsCount = holdings.Count,
                accountIdKey = accountIds.FirstOrDefault() ?? "",
                accountIds
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error analyzing portfolio");
            return StatusCode(500, new { message = "Error analyzing portfolio", detail = ex.Message });
        }
    }

    /// <summary>
    /// Calculate rebalancing trades needed
    /// </summary>
    [HttpPost("calculate")]
    public async Task<IActionResult> CalculateRebalance([FromBody] RebalanceRequest request)
    {
        if (string.IsNullOrEmpty(AccessToken))
            return Unauthorized(new { message = "Not authenticated" });

        try
        {
            var holdings = await GetAllHoldings(request.AccountIdKey);
            var cash = await GetCashBalance(request.AccountIdKey);
            var totalValue = holdings.Sum(h => h.MarketValue) + cash;

            var response = new RebalanceResponse
            {
                TotalPortfolioValue = totalValue
            };

            if (request.Model.RebalanceByAssetClass)
            {
                // Rebalance by asset class
                response = CalculateByAssetClass(holdings, cash, totalValue, request);
            }
            else
            {
                // Rebalance by specific symbols
                response = CalculateBySymbol(holdings, cash, totalValue, request);
            }

            return Ok(response);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error calculating rebalance");
            return StatusCode(500, new { message = "Error calculating rebalance", detail = ex.Message });
        }
    }

    private RebalanceResponse CalculateByAssetClass(List<HoldingInfo> holdings, decimal cash, decimal totalValue, RebalanceRequest request)
    {
        var response = new RebalanceResponse { TotalPortfolioValue = totalValue };
        var trades = new List<RebalanceTrade>();
        var warnings = new List<string>();

        // Current allocation by asset class
        var currentByClass = holdings
            .GroupBy(h => AssetClassMappings.GetAssetClass(h.Symbol))
            .ToDictionary(g => g.Key, g => g.ToList());

        foreach (var target in request.Model.Allocations.Where(a => a.AssetClass.HasValue))
        {
            var assetClass = target.AssetClass!.Value;
            var targetValue = totalValue * (target.TargetPercent / 100);
            var currentHoldings = currentByClass.GetValueOrDefault(assetClass, []);
            var currentValue = assetClass == AssetClass.Cash ? cash : currentHoldings.Sum(h => h.MarketValue);
            var difference = targetValue - currentValue;
            var differencePercent = totalValue > 0 ? (difference / totalValue) * 100 : 0;

            response.CurrentAllocations.Add(new TargetAllocation
            {
                AssetClass = assetClass,
                CurrentValue = currentValue,
                CurrentPercent = totalValue > 0 ? (currentValue / totalValue) * 100 : 0
            });

            response.TargetAllocations.Add(new TargetAllocation
            {
                AssetClass = assetClass,
                TargetPercent = target.TargetPercent,
                TargetValue = targetValue,
                CurrentValue = currentValue,
                CurrentPercent = totalValue > 0 ? (currentValue / totalValue) * 100 : 0,
                DifferenceValue = difference,
                DifferencePercent = differencePercent
            });

            // Skip cash - we don't trade cash directly
            if (assetClass == AssetClass.Cash) continue;

            // Check tolerance
            if (Math.Abs(differencePercent) <= request.TolerancePercent)
            {
                continue;
            }

            // Generate trades
            if (difference > request.MinimumTradeValue)
            {
                // Need to BUY more of this asset class
                // Pick the largest holding in this class to buy more, or suggest a default ETF
                var holdingToBuy = currentHoldings.OrderByDescending(h => h.MarketValue).FirstOrDefault();
                if (holdingToBuy != null)
                {
                    var qty = Math.Floor(difference / holdingToBuy.Price);
                    if (qty > 0)
                    {
                        trades.Add(new RebalanceTrade
                        {
                            Symbol = holdingToBuy.Symbol,
                            Description = holdingToBuy.Description,
                            Action = "BUY",
                            Quantity = qty,
                            EstimatedPrice = holdingToBuy.Price,
                            EstimatedValue = qty * holdingToBuy.Price,
                            AccountIdKey = holdingToBuy.AccountIdKey
                        });
                    }
                }
                else
                {
                    warnings.Add($"No existing holdings in {assetClass}. Consider adding a position manually.");
                }
            }
            else if (difference < -request.MinimumTradeValue && request.SellToRebalance)
            {
                // Need to SELL - this asset class is overweight
                var amountToSell = Math.Abs(difference);
                foreach (var holding in currentHoldings.OrderByDescending(h => h.MarketValue))
                {
                    if (amountToSell <= 0) break;
                    
                    var maxSellValue = Math.Min(amountToSell, holding.MarketValue * 0.9m); // Don't sell entire position
                    var qty = Math.Floor(maxSellValue / holding.Price);
                    
                    if (qty > 0)
                    {
                        trades.Add(new RebalanceTrade
                        {
                            Symbol = holding.Symbol,
                            Description = holding.Description,
                            Action = "SELL",
                            Quantity = qty,
                            EstimatedPrice = holding.Price,
                            EstimatedValue = qty * holding.Price,
                            AccountIdKey = holding.AccountIdKey
                        });
                        amountToSell -= qty * holding.Price;
                    }
                }
            }
        }

        response.RequiredTrades = trades;
        response.TradeCount = trades.Count;
        response.EstimatedTotalBuys = trades.Where(t => t.Action == "BUY").Sum(t => t.EstimatedValue);
        response.EstimatedTotalSells = trades.Where(t => t.Action == "SELL").Sum(t => t.EstimatedValue);
        response.Warnings = warnings;

        return response;
    }

    private RebalanceResponse CalculateBySymbol(List<HoldingInfo> holdings, decimal cash, decimal totalValue, RebalanceRequest request)
    {
        var response = new RebalanceResponse { TotalPortfolioValue = totalValue };
        var trades = new List<RebalanceTrade>();
        var warnings = new List<string>();

        var holdingsBySymbol = holdings.ToDictionary(h => h.Symbol, h => h);

        foreach (var target in request.Model.Allocations.Where(a => !string.IsNullOrEmpty(a.Symbol)))
        {
            var symbol = target.Symbol!;
            var targetValue = totalValue * (target.TargetPercent / 100);
            var currentHolding = holdingsBySymbol.GetValueOrDefault(symbol);
            var currentValue = currentHolding?.MarketValue ?? 0;
            var difference = targetValue - currentValue;
            var differencePercent = totalValue > 0 ? (difference / totalValue) * 100 : 0;

            response.CurrentAllocations.Add(new TargetAllocation
            {
                Symbol = symbol,
                CurrentValue = currentValue,
                CurrentPercent = totalValue > 0 ? (currentValue / totalValue) * 100 : 0
            });

            response.TargetAllocations.Add(new TargetAllocation
            {
                Symbol = symbol,
                TargetPercent = target.TargetPercent,
                TargetValue = targetValue,
                CurrentValue = currentValue,
                CurrentPercent = totalValue > 0 ? (currentValue / totalValue) * 100 : 0,
                DifferenceValue = difference,
                DifferencePercent = differencePercent
            });

            // Check tolerance
            if (Math.Abs(differencePercent) <= request.TolerancePercent)
                continue;

            if (Math.Abs(difference) < request.MinimumTradeValue)
                continue;

            if (currentHolding != null)
            {
                var qty = Math.Floor(Math.Abs(difference) / currentHolding.Price);
                if (qty > 0)
                {
                    trades.Add(new RebalanceTrade
                    {
                        Symbol = symbol,
                        Description = currentHolding.Description,
                        Action = difference > 0 ? "BUY" : "SELL",
                        Quantity = qty,
                        EstimatedPrice = currentHolding.Price,
                        EstimatedValue = qty * currentHolding.Price,
                        AccountIdKey = currentHolding.AccountIdKey
                    });
                }
            }
            else if (difference > 0)
            {
                warnings.Add($"Cannot buy {symbol} - need current price. Add position manually first.");
            }
        }

        response.RequiredTrades = trades;
        response.TradeCount = trades.Count;
        response.EstimatedTotalBuys = trades.Where(t => t.Action == "BUY").Sum(t => t.EstimatedValue);
        response.EstimatedTotalSells = trades.Where(t => t.Action == "SELL").Sum(t => t.EstimatedValue);
        response.Warnings = warnings;

        return response;
    }

    /// <summary>
    /// Preview or execute trades
    /// </summary>
    [HttpPost("execute")]
    public async Task<IActionResult> ExecuteTrades([FromBody] ExecuteRebalanceRequest request)
    {
        if (string.IsNullOrEmpty(AccessToken))
            return Unauthorized(new { message = "Not authenticated" });

        var results = new List<TradeExecutionResult>();

        foreach (var trade in request.Trades)
        {
            try
            {
                if (request.PreviewOnly)
                {
                    var previewResult = await PreviewOrder(trade);
                    results.Add(previewResult);
                }
                else
                {
                    var executeResult = await PlaceOrder(trade);
                    results.Add(executeResult);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error executing trade for {Symbol}", trade.Symbol);
                results.Add(new TradeExecutionResult
                {
                    Symbol = trade.Symbol,
                    Action = trade.Action,
                    Quantity = trade.Quantity,
                    Success = false,
                    ErrorMessage = ex.Message,
                    Status = "FAILED"
                });
            }
        }

        return Ok(new
        {
            previewOnly = request.PreviewOnly,
            results,
            successCount = results.Count(r => r.Success),
            failedCount = results.Count(r => !r.Success)
        });
    }

    private async Task<TradeExecutionResult> PreviewOrder(RebalanceTrade trade)
    {
        // Validate account ID
        if (string.IsNullOrEmpty(trade.AccountIdKey))
        {
            _logger.LogError("Cannot preview order for {Symbol}: AccountIdKey is empty", trade.Symbol);
            return new TradeExecutionResult
            {
                Symbol = trade.Symbol,
                Action = trade.Action,
                Quantity = trade.Quantity,
                Success = false,
                Status = "FAILED",
                ErrorMessage = "Account ID is missing. Please reload the page and try again."
            };
        }

        var consumerKey = _config["ETrade:ConsumerKey"]!;
        var consumerSecret = _config["ETrade:ConsumerSecret"]!;

        var orderXml = BuildOrderXml(trade, isPreview: true);
        var url = $"{ApiBaseUrl}/v1/accounts/{trade.AccountIdKey}/orders/preview.json";

        _logger.LogInformation("Previewing order for {Symbol}: URL={Url}, AccountIdKey={AccountId}", 
            trade.Symbol, url, trade.AccountIdKey);

        using var client = new HttpClient();
        var (authHeader, _) = BuildOAuthHeader(consumerKey, consumerSecret, AccessToken!, AccessTokenSecret!, "POST", url, null);
        client.DefaultRequestHeaders.Add("Authorization", authHeader);
        client.DefaultRequestHeaders.Add("Accept", "application/json");

        var content = new StringContent(orderXml, Encoding.UTF8, "application/xml");
        var response = await client.PostAsync(url, content);
        var responseContent = await response.Content.ReadAsStringAsync();

        _logger.LogInformation("Preview order response for {Symbol}: {Status} - {Response}", 
            trade.Symbol, response.StatusCode, responseContent);

        if (response.IsSuccessStatusCode)
        {
            return new TradeExecutionResult
            {
                Symbol = trade.Symbol,
                Action = trade.Action,
                Quantity = trade.Quantity,
                Success = true,
                Status = "PREVIEW",
                PreviewId = ExtractPreviewId(responseContent)
            };
        }

        // Parse E*TRADE error for better messaging
        var errorMessage = ParseETradeError(responseContent);

        return new TradeExecutionResult
        {
            Symbol = trade.Symbol,
            Action = trade.Action,
            Quantity = trade.Quantity,
            Success = false,
            Status = "FAILED",
            ErrorMessage = errorMessage
        };
    }

    private async Task<TradeExecutionResult> PlaceOrder(RebalanceTrade trade)
    {
        // Validate account ID
        if (string.IsNullOrEmpty(trade.AccountIdKey))
        {
            _logger.LogError("Cannot place order for {Symbol}: AccountIdKey is empty", trade.Symbol);
            return new TradeExecutionResult
            {
                Symbol = trade.Symbol,
                Action = trade.Action,
                Quantity = trade.Quantity,
                Success = false,
                Status = "FAILED",
                ErrorMessage = "Account ID is missing. Please reload the page and try again."
            };
        }

        var consumerKey = _config["ETrade:ConsumerKey"]!;
        var consumerSecret = _config["ETrade:ConsumerSecret"]!;

        // First preview to get previewId
        var previewResult = await PreviewOrder(trade);
        if (!previewResult.Success || string.IsNullOrEmpty(previewResult.PreviewId))
        {
            return new TradeExecutionResult
            {
                Symbol = trade.Symbol,
                Action = trade.Action,
                Quantity = trade.Quantity,
                Success = false,
                Status = "FAILED",
                ErrorMessage = "Failed to preview order: " + previewResult.ErrorMessage
            };
        }

        // Now place the order
        var orderXml = BuildOrderXml(trade, isPreview: false, previewResult.PreviewId);
        var url = $"{ApiBaseUrl}/v1/accounts/{trade.AccountIdKey}/orders/place.json";

        _logger.LogInformation("Placing order for {Symbol}: URL={Url}", trade.Symbol, url);

        using var client = new HttpClient();
        var (authHeader, _) = BuildOAuthHeader(consumerKey, consumerSecret, AccessToken!, AccessTokenSecret!, "POST", url, null);
        client.DefaultRequestHeaders.Add("Authorization", authHeader);
        client.DefaultRequestHeaders.Add("Accept", "application/json");

        var content = new StringContent(orderXml, Encoding.UTF8, "application/xml");
        var response = await client.PostAsync(url, content);
        var responseContent = await response.Content.ReadAsStringAsync();

        _logger.LogInformation("Place order response for {Symbol}: {Status} - {Response}", 
            trade.Symbol, response.StatusCode, responseContent);

        if (response.IsSuccessStatusCode)
        {
            return new TradeExecutionResult
            {
                Symbol = trade.Symbol,
                Action = trade.Action,
                Quantity = trade.Quantity,
                Success = true,
                Status = "PLACED",
                OrderId = ExtractOrderId(responseContent)
            };
        }

        return new TradeExecutionResult
        {
            Symbol = trade.Symbol,
            Action = trade.Action,
            Quantity = trade.Quantity,
            Success = false,
            Status = "FAILED",
            ErrorMessage = responseContent
        };
    }

    private string BuildOrderXml(RebalanceTrade trade, bool isPreview, string? previewId = null)
    {
        var orderAction = trade.Action == "BUY" ? "BUY" : "SELL";
        var priceType = trade.OrderType == "LIMIT" ? "LIMIT" : "MARKET";

        var xml = $@"<{(isPreview ? "PreviewOrderRequest" : "PlaceOrderRequest")}>
  <orderType>EQ</orderType>
  <clientOrderId>{Guid.NewGuid():N}</clientOrderId>
  {(previewId != null ? $"<PreviewIds><previewId>{previewId}</previewId></PreviewIds>" : "")}
  <Order>
    <allOrNone>false</allOrNone>
    <priceType>{priceType}</priceType>
    <orderTerm>GOOD_FOR_DAY</orderTerm>
    <marketSession>REGULAR</marketSession>
    {(trade.LimitPrice.HasValue ? $"<limitPrice>{trade.LimitPrice.Value}</limitPrice>" : "")}
    <Instrument>
      <Product>
        <securityType>EQ</securityType>
        <symbol>{trade.Symbol}</symbol>
      </Product>
      <orderAction>{orderAction}</orderAction>
      <quantityType>QUANTITY</quantityType>
      <quantity>{trade.Quantity}</quantity>
    </Instrument>
  </Order>
</{(isPreview ? "PreviewOrderRequest" : "PlaceOrderRequest")}>";

        return xml;
    }

    private string? ExtractPreviewId(string jsonResponse)
    {
        try
        {
            var doc = JsonDocument.Parse(jsonResponse);
            if (doc.RootElement.TryGetProperty("PreviewOrderResponse", out var resp) &&
                resp.TryGetProperty("PreviewIds", out var ids) &&
                ids.TryGetProperty("previewId", out var id))
            {
                return id.GetInt64().ToString();
            }
        }
        catch { }
        return null;
    }

    private string? ExtractOrderId(string jsonResponse)
    {
        try
        {
            var doc = JsonDocument.Parse(jsonResponse);
            if (doc.RootElement.TryGetProperty("PlaceOrderResponse", out var resp) &&
                resp.TryGetProperty("OrderIds", out var ids) &&
                ids.TryGetProperty("orderId", out var id))
            {
                return id.GetInt64().ToString();
            }
        }
        catch { }
        return null;
    }

    private string ParseETradeError(string responseContent)
    {
        try
        {
            var doc = JsonDocument.Parse(responseContent);
            if (doc.RootElement.TryGetProperty("Error", out var error))
            {
                var code = error.TryGetProperty("code", out var c) ? c.GetInt32() : 0;
                var message = error.TryGetProperty("message", out var m) ? m.GetString() : responseContent;

                // Provide user-friendly messages for known error codes
                return code switch
                {
                    100 => "E*TRADE service temporarily unavailable. This often happens outside market hours (9:30 AM - 4:00 PM ET) or during maintenance. Please try again later.",
                    102 => "Invalid session. Please sign out and sign back in.",
                    103 => "Order rejected: " + message,
                    _ => message ?? responseContent
                };
            }
        }
        catch { }

        return responseContent;
    }

    // Helper classes
    private class HoldingInfo
    {
        public string Symbol { get; set; } = "";
        public string Description { get; set; } = "";
        public decimal Quantity { get; set; }
        public decimal Price { get; set; }
        public decimal MarketValue { get; set; }
        public string AccountIdKey { get; set; } = "";
    }

    private async Task<List<HoldingInfo>> GetAllHoldings(string? accountIdKey)
    {
        // Call the portfolio controller's positions endpoint
        var consumerKey = _config["ETrade:ConsumerKey"]!;
        var consumerSecret = _config["ETrade:ConsumerSecret"]!;

        var holdings = new List<HoldingInfo>();

        // Get account list if no specific account provided
        var accountKeys = new List<string>();
        if (string.IsNullOrEmpty(accountIdKey))
        {
            using var client = new HttpClient();
            var listUrl = $"{ApiBaseUrl}/v1/accounts/list.json";
            var (authHeader, _) = BuildOAuthHeader(consumerKey, consumerSecret, AccessToken!, AccessTokenSecret!, "GET", listUrl, null);
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
        }
        else
        {
            accountKeys.Add(accountIdKey);
        }

        // Get positions for each account
        foreach (var acctKey in accountKeys)
        {
            using var client = new HttpClient();
            var portfolioUrl = $"{ApiBaseUrl}/v1/accounts/{acctKey}/portfolio.json";
            var (authHeader, _) = BuildOAuthHeader(consumerKey, consumerSecret, AccessToken!, AccessTokenSecret!, "GET", portfolioUrl, null);
            client.DefaultRequestHeaders.Add("Authorization", authHeader);

            var response = await client.GetAsync(portfolioUrl);
            var content = await response.Content.ReadAsStringAsync();

            if (response.IsSuccessStatusCode && !string.IsNullOrWhiteSpace(content))
            {
                try
                {
                    var doc = JsonDocument.Parse(content);
                    if (doc.RootElement.TryGetProperty("PortfolioResponse", out var portfolioResp) &&
                        portfolioResp.TryGetProperty("AccountPortfolio", out var accountPortfolios))
                    {
                        foreach (var ap in accountPortfolios.EnumerateArray())
                        {
                            if (ap.TryGetProperty("Position", out var positions))
                            {
                                foreach (var pos in positions.EnumerateArray())
                                {
                                    var holding = new HoldingInfo { AccountIdKey = acctKey };

                                    if (pos.TryGetProperty("Product", out var product) &&
                                        product.TryGetProperty("symbol", out var symbol))
                                    {
                                        holding.Symbol = symbol.GetString() ?? "";
                                    }

                                    if (pos.TryGetProperty("symbolDescription", out var desc))
                                        holding.Description = desc.GetString() ?? "";

                                    if (pos.TryGetProperty("quantity", out var qty))
                                        holding.Quantity = qty.GetDecimal();

                                    if (pos.TryGetProperty("marketValue", out var mv))
                                        holding.MarketValue = mv.GetDecimal();

                                    // Get price
                                    if (pos.TryGetProperty("Quick", out var quick) &&
                                        quick.TryGetProperty("lastTrade", out var price))
                                    {
                                        holding.Price = price.GetDecimal();
                                }
                                else if (holding.Quantity > 0)
                                {
                                    holding.Price = holding.MarketValue / holding.Quantity;
                                }

                                if (!string.IsNullOrEmpty(holding.Symbol))
                                    holdings.Add(holding);
                            }
                        }
                    }
                }
                }
                catch (JsonException ex)
                {
                    _logger.LogWarning(ex, "Failed to parse portfolio for account {AccountKey}", acctKey);
                }
            }
        }

        return holdings;
    }

    private async Task<List<string>> GetAccountIds(string? accountIdKey)
    {
        if (!string.IsNullOrEmpty(accountIdKey))
            return [accountIdKey];

        var consumerKey = _config["ETrade:ConsumerKey"]!;
        var consumerSecret = _config["ETrade:ConsumerSecret"]!;
        var accountKeys = new List<string>();

        using var client = new HttpClient();
        var listUrl = $"{ApiBaseUrl}/v1/accounts/list.json";
        var (authHeader, _) = BuildOAuthHeader(consumerKey, consumerSecret, AccessToken!, AccessTokenSecret!, "GET", listUrl, null);
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

    private async Task<decimal> GetCashBalance(string? accountIdKey)
    {
        var consumerKey = _config["ETrade:ConsumerKey"]!;
        var consumerSecret = _config["ETrade:ConsumerSecret"]!;
        decimal totalCash = 0;

        // Get account list
        var accountKeys = new List<string>();
        if (string.IsNullOrEmpty(accountIdKey))
        {
            using var client = new HttpClient();
            var listUrl = $"{ApiBaseUrl}/v1/accounts/list.json";
            var (authHeader, _) = BuildOAuthHeader(consumerKey, consumerSecret, AccessToken!, AccessTokenSecret!, "GET", listUrl, null);
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
        }
        else
        {
            accountKeys.Add(accountIdKey);
        }

        // Get balance for each account
        foreach (var acctKey in accountKeys)
        {
            using var client = new HttpClient();
            var balUrl = $"{ApiBaseUrl}/v1/accounts/{acctKey}/balance.json";
            var queryParams = new SortedDictionary<string, string>
            {
                { "instType", "BROKERAGE" },
                { "realTimeNAV", "true" }
            };
            var fullUrl = balUrl + "?" + string.Join("&", queryParams.Select(kvp => $"{kvp.Key}={Uri.EscapeDataString(kvp.Value)}"));

            var (authHeader, _) = BuildOAuthHeader(consumerKey, consumerSecret, AccessToken!, AccessTokenSecret!, "GET", balUrl, queryParams);
            client.DefaultRequestHeaders.Add("Authorization", authHeader);

            var response = await client.GetAsync(fullUrl);
            var content = await response.Content.ReadAsStringAsync();

            if (response.IsSuccessStatusCode)
            {
                var doc = JsonDocument.Parse(content);
                if (doc.RootElement.TryGetProperty("BalanceResponse", out var balResp) &&
                    balResp.TryGetProperty("Computed", out var computed))
                {
                    if (computed.TryGetProperty("cashAvailableForInvestment", out var cash))
                        totalCash += cash.GetDecimal();
                    else if (computed.TryGetProperty("cashBalance", out var cashBal))
                        totalCash += cashBal.GetDecimal();
                }
            }
        }

        return Math.Max(0, totalCash);
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
