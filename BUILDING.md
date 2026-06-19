# Building Rust+ Desktop Locally

If you are a developer looking to compile and build **Rust+ Desktop** from the source code, you'll need to set up a few things first. Some sensitive files and proprietary assets are intentionally ignored via `.gitignore`.

## Prerequisites
- **IDE:** Visual Studio 2022 (recommended) or JetBrains Rider
- **Framework:** .NET Desktop Development Workload (.NET 7 / .NET 8)
- **Node.js:** Included automatically via the `runtime/` folder during the build process.

---

## 1. Missing Secrets (`ObfuscatedSecrets.cs`)
Because Rust+ Desktop integrates with Supabase, Discord, and Firebase, the actual production API keys are encrypted and excluded from the open-source repository.

To successfully compile the project, you need to provide a dummy file so the compiler doesn't throw errors:
1. Navigate to `RustPlusDesktop/Services/Data/`
2. Create a new file named `ObfuscatedSecrets.cs`
3. Insert the following stub code:

```csharp
namespace RustPlusDesk.Services.Data
{
    public static class ObfuscatedSecrets
    {
        public static string GetSupabaseUrl() => "YOUR_TEST_SUPABASE_URL";
        public static string GetSupabaseKey() => "YOUR_TEST_SUPABASE_KEY";
        public static string GetDiscordClientId() => "YOUR_TEST_DISCORD_CLIENT_ID";
    }
}
```
*(If you are an official maintainer, simply paste the original `ObfuscatedSecrets.cs` from your backup into this folder).*

## 2. Environment Variables (`.env`)
The app expects an environment file in the root directory. 
Simply create a blank `.env` file in the main `RustPlusDesktop` directory, or use your own development configuration.

## 3. The 3D Map Parser
Rust+ Desktop v7.0.0 introduced an interactive 2D/3D Map engine. **Please note that the 3D Map Parser module is proprietary intellectual property and is NOT open-source.** It is integrated into Rust+ Desktop under a specific license and is therefore entirely excluded from this repository.

**What this means for local builds:**
- You do **not** need the `MapParser` folder to successfully compile and run the application.
- The build will succeed without it.
- However, the 3D Map functionality will be unavailable/disabled in your local build.

---

## Building
Once you have created the `ObfuscatedSecrets.cs` stub and added a `.env` file, you can simply open `RustPlusDesk.sln` in Visual Studio and hit **F5** to build and run the project!
