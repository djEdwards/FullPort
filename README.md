# FullPort - E*TRADE Portfolio Management Platform

<div align="center">

![.NET](https://img.shields.io/badge/.NET-10.0-512BD4?style=for-the-badge&logo=dotnet)
![C#](https://img.shields.io/badge/C%23-14.0-239120?style=for-the-badge&logo=csharp)
![Bootstrap](https://img.shields.io/badge/Bootstrap-5.3-7952B3?style=for-the-badge&logo=bootstrap)

**A comprehensive, portfolio management application for E*TRADE accounts**

[Features](#-features) • [Getting Started](#-getting-started) • [Configuration](#-configuration) • [Usage](#-usage) • [API Reference](#-api-reference) • [Developer Guide](GUIDE.md)

</div>

---

## 📋 Overview

FullPort is a full-featured portfolio management platform that integrates with E*TRADE's API to provide real-time portfolio analysis, intelligent rebalancing recommendations, and automated trade execution. Built with modern .NET 10 and a responsive Bootstrap 5 UI, it offers institutional-quality portfolio management tools for individual investors.

### Key Highlights

- 🔐 **Secure OAuth 1.0a Authentication** - Industry-standard E*TRADE API integration
- 📊 **Real-Time Market Data** - Live quotes, market indices, and portfolio valuations
- ⚖️ **Intelligent Rebalancing** - 9+ pre-built strategies plus custom allocation support
- 🏗️ **Portfolio Builder** - Build new positions with strategy-based recommendations
- 📈 **Advanced Analytics** - Performance tracking, asset allocation analysis, and more
- 💹 **Trade Execution** - Preview and execute trades directly through the platform

---

## ✨ Features

### 🏠 Dashboard

The main dashboard provides a comprehensive overview of your portfolio:

- **Portfolio Value** - Real-time total portfolio valuation
- **Daily Change** - Today's gain/loss with percentage
- **Cash Balance** - Available cash for investment
- **Market Indices** - Live S&P 500, Dow Jones, and NASDAQ data
- **Personalized Greeting** - Time-based greeting with customizable name
- **Quick Authentication** - One-click E*TRADE OAuth flow with auto-closing popup

### 📈 Holdings Management

Complete visibility into your positions:

- **Position List** - All holdings with real-time prices and values
- **Sorting** - Sort by symbol, value, quantity, or daily change
- **Asset Classification** - Automatic categorization by asset class
- **Multi-Account Support** - Aggregate view across all E*TRADE accounts
- **Performance Metrics** - Daily gain/loss per position

### ⚖️ Portfolio Rebalancing

Sophisticated rebalancing engine with multiple strategies:

#### Pre-Built Strategies

| Strategy | Description | Allocation |
|----------|-------------|------------|
| **Ultra-Conservative** | Capital preservation | 10% stocks, 60% bonds, 20% cash |
| **Conservative** | Low risk | 25% stocks, 50% bonds, 10% real estate |
| **Income** | Yield-focused | Dividends, bonds, REITs |
| **Balanced** | Classic 60/40 | 60% stocks, 30% bonds, 10% alternatives |
| **Moderate** | Balanced growth | 50% stocks, 25% bonds, alternatives + crypto |
| **Dividend Growth** | Quality dividends | Dividend aristocrats + REITs |
| **Growth** | Capital appreciation | 70% stocks, growth focus + crypto |
| **Aggressive** | High risk | 65% stocks, 10% crypto, minimal bonds |
| **Ultra-Aggressive** | Maximum growth | 60% stocks, 25% crypto, emerging markets |

#### Custom Allocation

- **Full Control** - Set exact percentages for each asset class
- **Validation** - Must equal 100% before proceeding
- **Quick Presets** - One-click 60/40, Growth, Conservative, Income presets
- **Visual Feedback** - Real-time total calculation with validation status

#### Intelligent Trade Generation

- **Smart Selling** - Only sells enough to fund planned buys
- **Position Protection** - Caps single-position sells at 50%
- **Auto ETF Recommendations** - Suggests default ETFs for missing asset classes:
  - US Stocks → VTI (Vanguard Total Stock Market)
  - International → VXUS (Vanguard Total International)
  - Bonds → BND (Vanguard Total Bond Market)
  - Real Estate → VNQ (Vanguard Real Estate)
  - Commodities → GLD (SPDR Gold Shares)
  - Crypto → IBIT (iShares Bitcoin Trust)
- **Minimum Trade Enforcement** - Configurable minimum trade value
- **Tolerance Bands** - Skip rebalancing within tolerance threshold

### 🏗️ Portfolio Builder

Build new positions from scratch or add to existing portfolios:

- **Strategy-Based Building** - Select a strategy and auto-populate ETFs
- **Custom Allocation** - Define your own asset class percentages
- **Individual Stock Addition** - Add any symbol with dollar amount or share count
- **Real-Time Quotes** - Fetch current prices before adding
- **Auto-Rebalancing** - Proportionally adjust existing allocations
- **Save/Load Portfolios** - Persist portfolio configurations locally
- **Cash Simulation** - Test with simulated cash balances (dev mode)

### 📊 Analytics

Comprehensive portfolio analytics and insights:

- **Asset Allocation Charts** - Visual breakdown by asset class
- **Diversification Scoring** - Portfolio concentration analysis
- **Performance Tracking** - Historical performance metrics
- **Risk Assessment** - Portfolio risk profile evaluation

### 💹 Trade Execution

Full trading capabilities through E*TRADE:

- **Order Preview** - See estimated execution details before placing
- **Market Orders** - Execute at current market price
- **Limit Orders** - Set your target price
- **Batch Execution** - Execute multiple trades in sequence
- **Error Handling** - User-friendly error messages with guidance
- **Order Status** - Track placed orders and confirmations

### ⚙️ Settings

Customizable application settings:

- **Minimum Trade Value** - Set minimum dollar amount per trade (default: $50)
- **Tolerance Percentage** - Rebalancing threshold (default: 2%)
- **Display Preferences** - UI customization options
- **Persistent Storage** - Settings saved to browser localStorage

---

## 🚀 Getting Started

### Prerequisites

- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0)
- E*TRADE Developer Account with API access
- E*TRADE Consumer Key and Consumer Secret

### Installation

1. **Clone the repository**
   ```bash
   git clone https://github.com/yourusername/fullport.git
   cd fullport
   ```

2. **Configure API credentials**
   
   Create or update `appsettings.json`:
   ```json
   {
     "ETrade": {
       "ConsumerKey": "YOUR_CONSUMER_KEY",
       "ConsumerSecret": "YOUR_CONSUMER_SECRET",
       "UseSandbox": false
     }
   }
   ```

   Or use User Secrets (recommended for development):
   ```bash
   dotnet user-secrets set "ETrade:ConsumerKey" "YOUR_CONSUMER_KEY"
   dotnet user-secrets set "ETrade:ConsumerSecret" "YOUR_CONSUMER_SECRET"
   ```

3. **Run the application**
   ```bash
   dotnet run
   ```

4. **Open in browser**
   ```
   https://localhost:5001
   ```

### E*TRADE API Setup

1. Visit [E*TRADE Developer Portal](https://developer.etrade.com/)
2. Create a new application
3. Request production API access (sandbox available for testing)
4. Copy your Consumer Key and Consumer Secret
5. Configure callback URL: `oob` (out-of-band)

---

## ⚙️ Configuration

### appsettings.json

```json
{
  "Logging": {
    "LogLevel": {
      "Default": "Information",
      "Microsoft.AspNetCore": "Warning"
    }
  },
  "ETrade": {
    "ConsumerKey": "",
    "ConsumerSecret": "",
    "UseSandbox": false
  },
  "AllowedHosts": "*"
}
```

### Configuration Options

| Setting | Description | Default |
|---------|-------------|---------|
| `ETrade:ConsumerKey` | Your E*TRADE API consumer key | Required |
| `ETrade:ConsumerSecret` | Your E*TRADE API consumer secret | Required |
| `ETrade:UseSandbox` | Use E*TRADE sandbox environment | `false` |

### Environment Variables

All settings can be overridden with environment variables:

```bash
export ETrade__ConsumerKey="your_key"
export ETrade__ConsumerSecret="your_secret"
export ETrade__UseSandbox="true"
```

---

## 📖 Usage

### Authentication Flow

1. Click **"Sign in with E*TRADE"** on the Dashboard
2. A popup opens to E*TRADE's authorization page
3. Log in with your E*TRADE credentials
4. Authorize the application
5. Copy the verification PIN displayed
6. Paste the PIN into FullPort and click Submit
7. The popup closes automatically upon success

### Rebalancing Your Portfolio

1. Navigate to the **Rebalance** tab
2. Select a pre-built strategy or **Custom**
3. If Custom, set percentages for each asset class (must total 100%)
4. Configure options:
   - ✅ Allow Selling - Enable selling overweight positions
   - Minimum Trade - Skip trades below this value
   - Tolerance - Skip if allocation is within this percentage
5. Click **Calculate Trades**
6. Review recommended trades
7. Click **Preview Orders** to verify with E*TRADE
8. Click **Execute All Trades** to place orders

### Building New Positions

1. Navigate to **Build New Positions** mode
2. Select a strategy or create custom allocation
3. Add individual stocks/ETFs if desired
4. Click **Calculate Build**
5. Review and adjust quantities
6. Execute trades to build positions

### Development Mode Features

When running in Development environment:

- **Test Cash Simulation** - Override cash balance for testing
- **Quick Cash Buttons** - $1K, $5K, $10K, $50K, $100K presets
- **Detailed Logging** - Full API request/response logging
- **Error Details** - Verbose error messages

---

## 🔌 API Reference

### Portfolio Controller

| Endpoint | Method | Description |
|----------|--------|-------------|
| `/api/portfolio/authenticate` | GET | Start OAuth flow, returns authorize URL |
| `/api/portfolio/access-token` | POST | Exchange PIN for access token |
| `/api/portfolio/accounts` | GET | List all E*TRADE accounts |
| `/api/portfolio/positions` | GET | Get all positions across accounts |
| `/api/portfolio/balance` | GET | Get account balance information |

### Rebalance Controller

| Endpoint | Method | Description |
|----------|--------|-------------|
| `/api/rebalance/models` | GET | Get all pre-built rebalancing strategies |
| `/api/rebalance/analyze` | GET | Analyze current portfolio allocation |
| `/api/rebalance/calculate` | POST | Calculate required trades for rebalancing |
| `/api/rebalance/execute` | POST | Preview or execute trades |

### Market Controller

| Endpoint | Method | Description |
|----------|--------|-------------|
| `/api/market/quote/{symbols}` | GET | Get quotes for one or more symbols |
| `/api/market/indices` | GET | Get major market indices |
| `/api/market/suggested-etfs` | GET | Get suggested ETFs by asset class |

### Analytics Controller

| Endpoint | Method | Description |
|----------|--------|-------------|
| `/api/analytics/allocation` | GET | Get portfolio allocation breakdown |
| `/api/analytics/performance` | GET | Get performance metrics |
| `/api/analytics/diversification` | GET | Get diversification analysis |

---

## 🏗️ Architecture

### Project Structure

```
FullPort/
├── Controllers/
│   ├── PortfolioController.cs    # Authentication & account management
│   ├── RebalanceController.cs    # Rebalancing logic & trade calculation
│   ├── MarketController.cs       # Market data & quotes
│   └── AnalyticsController.cs    # Portfolio analytics
├── Models/
│   ├── RebalanceModels.cs        # Rebalancing DTOs & enums
│   └── AssetClassMappings.cs     # Symbol-to-asset-class mappings
├── wwwroot/
│   └── index.html                # Single-page application UI
├── Program.cs                     # Application entry point
├── appsettings.json              # Configuration
└── README.md                      # This file
```

### Technology Stack

- **Backend**: ASP.NET Core 10 Web API
- **Frontend**: Bootstrap 5, Vanilla JavaScript
- **Authentication**: OAuth 1.0a (E*TRADE)
- **Data Format**: JSON
- **Styling**: Custom CSS with dark theme

### Asset Class Taxonomy

| Asset Class | Description | Example ETFs |
|-------------|-------------|--------------|
| `USStock` | US Equities | VTI, VOO, SPY |
| `InternationalStock` | Non-US Equities | VXUS, EFA, VEA |
| `Bond` | Fixed Income | BND, AGG, TLT |
| `RealEstate` | REITs | VNQ, SCHH, IYR |
| `Commodity` | Commodities & Gold | GLD, IAU, GSG |
| `Crypto` | Cryptocurrency ETFs | IBIT, BITO, GBTC |
| `Cash` | Cash & Equivalents | Money Market |

---

## 🔒 Security

### Best Practices Implemented

- ✅ OAuth 1.0a signed requests
- ✅ No credential storage in browser
- ✅ HTTPS required for all API calls
- ✅ User secrets for development credentials
- ✅ Input validation on all endpoints
- ✅ Error messages sanitized for production

### Security Recommendations

- Use environment variables for production credentials
- Enable HTTPS in production
- Implement session timeouts
- Add rate limiting for API endpoints
- Consider adding 2FA for sensitive operations

---

## 🧪 Testing

### Development Mode Testing

1. Enable test cash simulation:
   ```javascript
   setTestCash(10000); // Simulate $10,000 cash
   ```

2. Use E*TRADE Sandbox:
   ```json
   {
     "ETrade": {
       "UseSandbox": true
     }
   }
   ```

