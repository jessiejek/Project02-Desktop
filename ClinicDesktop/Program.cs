using System.Diagnostics;
using System.Net.Http;
using System.Security.Cryptography;
using Photino.NET;

// ClinicDesktop — the clinic "node" as a desktop app.
// Starts the LOCAL clinic system (the same .NET API on a SQLite file + the same web UI) and shows it in a native window,
// so the doctor can log in, register patients and run consultations with no internet. (Syncing to the cloud is the next phase.)
//
//   dotnet run -- --api <dir with ClinicApp.Api.dll> --ui <dir with the Next.js standalone server.js> [--data <dir>] [--demo]
//   (or the CLINIC_API_DIR / CLINIC_UI_DIR / CLINIC_DATA_DIR environment variables; or a ./payload/api and ./payload/ui next to the program)
//
//   --demo   runs the API in Development so the sample accounts (doctor@clinic.test, ...) exist. NEVER for a real clinic.

const int ApiPort = 5199;   // fixed: the web UI is built with this API address baked in (NEXT_PUBLIC_API_URL)
const int UiPort = 3199;

string? Arg(string name)
{
    var a = Environment.GetCommandLineArgs();
    var i = Array.IndexOf(a, name);
    return i >= 0 && i + 1 < a.Length ? a[i + 1] : null;
}

var demo = Environment.GetCommandLineArgs().Contains("--demo");
var baseDir = AppContext.BaseDirectory;
var apiDir = Arg("--api") ?? Environment.GetEnvironmentVariable("CLINIC_API_DIR") ?? Path.Combine(baseDir, "payload", "api");
var uiDir = Arg("--ui") ?? Environment.GetEnvironmentVariable("CLINIC_UI_DIR") ?? Path.Combine(baseDir, "payload", "ui");
var dataDir = Arg("--data") ?? Environment.GetEnvironmentVariable("CLINIC_DATA_DIR")
    ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ClinicNode");
var nodeExe = Environment.GetEnvironmentVariable("CLINIC_NODE_EXE") ?? "node";

Directory.CreateDirectory(dataDir);
var logDir = Path.Combine(dataDir, "logs");
Directory.CreateDirectory(logDir);

// ── the node's own signing secret, made once on first run and kept in the data folder (never leaves this computer)
var secretFile = Path.Combine(dataDir, "node-secret.key");
if (!File.Exists(secretFile))
    File.WriteAllText(secretFile, Convert.ToHexString(RandomNumberGenerator.GetBytes(32)).ToLowerInvariant());
var secret = File.ReadAllText(secretFile).Trim();

var apiUrl = $"http://localhost:{ApiPort}";
var uiUrl = $"http://localhost:{UiPort}";
var children = new List<Process>();

Process Start(string name, string fileName, string args, string workDir, IDictionary<string, string> env)
{
    var psi = new ProcessStartInfo(fileName, args)
    {
        WorkingDirectory = workDir, UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true,
    };
    foreach (var (k, v) in env) psi.Environment[k] = v;
    var p = Process.Start(psi) ?? throw new InvalidOperationException($"Could not start {name}.");
    var log = new StreamWriter(Path.Combine(logDir, $"{name}.log"), append: false) { AutoFlush = true };
    p.OutputDataReceived += (_, e) => { if (e.Data is not null) log.WriteLine(e.Data); };
    p.ErrorDataReceived += (_, e) => { if (e.Data is not null) log.WriteLine(e.Data); };
    p.BeginOutputReadLine();
    p.BeginErrorReadLine();
    children.Add(p);
    return p;
}

void StopAll()
{
    foreach (var p in children)
    {
        try { if (!p.HasExited) p.Kill(entireProcessTree: true); } catch { /* already gone */ }
    }
}

async Task<bool> WaitFor(string url, TimeSpan timeout)
{
    using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(3) };
    var until = DateTime.UtcNow + timeout;
    while (DateTime.UtcNow < until)
    {
        try { using var r = await http.GetAsync(url); if ((int)r.StatusCode < 500) return true; } catch { /* not up yet */ }
        await Task.Delay(500);
    }
    return false;
}

static string Page(string title, string line) => $@"<html><body style='font-family:system-ui;background:#f8f9ff;color:#0f172a;display:flex;align-items:center;justify-content:center;height:100vh;margin:0'>
<div style='text-align:center;max-width:520px'><h2 style='color:#00685f'>{title}</h2><p>{line}</p></div></body></html>";

var window = new PhotinoWindow()
    .SetTitle("Dr. Grace Gavino Medical Clinic")
    .SetUseOsDefaultSize(false)
    .SetSize(1280, 820)
    .Center()
    .SetResizable(true)
    .LoadRawString(Page("Starting the clinic system…", "Preparing the local database. This takes a few seconds."));

// start everything in the background so the window appears immediately
_ = Task.Run(async () =>
{
    try
    {
        if (!File.Exists(Path.Combine(apiDir, "ClinicApp.Api.dll"))) throw new FileNotFoundException($"ClinicApp.Api.dll not found in {apiDir}");
        if (!File.Exists(Path.Combine(uiDir, "server.js"))) throw new FileNotFoundException($"server.js not found in {uiDir}");

        Start("api", "dotnet", $"ClinicApp.Api.dll --urls {apiUrl}", apiDir, new Dictionary<string, string>
        {
            ["ASPNETCORE_ENVIRONMENT"] = demo ? "Development" : "Production",
            ["Database__Provider"] = "Sqlite",
            ["ConnectionStrings__DefaultConnection"] = $"Data Source={Path.Combine(dataDir, "clinic-node.db")}",
            ["Jwt__Secret"] = secret,
            ["Cors__AllowedOrigins__0"] = uiUrl,
        });

        Start("ui", nodeExe, "server.js", uiDir, new Dictionary<string, string>
        {
            ["PORT"] = UiPort.ToString(), ["HOSTNAME"] = "localhost", ["NODE_ENV"] = "production",
            ["JWT_SECRET"] = secret, ["JWT_ISSUER"] = "ClinicApp", ["JWT_AUDIENCE"] = "ClinicApp", ["DOTNET_API_URL"] = apiUrl,
            ["CLINIC_LOCAL_NODE"] = "1", // plain-http localhost: login cookies must not be Secure-only (Safari / WebKit refuses them)
        });

        var apiOk = await WaitFor($"{apiUrl}/health", TimeSpan.FromSeconds(90));
        var uiOk = await WaitFor($"{uiUrl}/login", TimeSpan.FromSeconds(90));
        if (!apiOk || !uiOk)
            throw new TimeoutException($"The clinic system did not start (api: {(apiOk ? "ok" : "no")}, ui: {(uiOk ? "ok" : "no")}). See {logDir}");

        window.Invoke(() => window.Load(new Uri($"{uiUrl}/login")));
    }
    catch (Exception ex)
    {
        window.Invoke(() => window.LoadRawString(Page("The clinic system could not start", System.Net.WebUtility.HtmlEncode(ex.Message))));
    }
});

try { window.WaitForClose(); }
finally { StopAll(); }
