using System.Threading;
using IBApi;

string host = args.Length > 0 ? args[0] : "127.0.0.1";
int port = args.Length > 1 && int.TryParse(args[1], out int parsedPort) ? parsedPort : 7497;
int clientId = args.Length > 2 && int.TryParse(args[2], out int parsedClientId) ? parsedClientId : 117;

var wrapper = new ProbeWrapper();
var signal = new EReaderMonitorSignal();
var client = new EClientSocket(wrapper, signal);

Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] IBKR probe connecting to {host}:{port} with clientId {clientId}...");
client.eConnect(host, port, clientId);

if (!client.IsConnected())
{
    Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] TCP connect failed. Check TWS API port and whether TWS is listening.");
    Console.WriteLine("Press Enter to close.");
    Console.ReadLine();
    return;
}

Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] TCP socket opened. Waiting for nextValidId handshake...");

var reader = new EReader(client, signal);
reader.Start();
var readerThread = new Thread(() =>
{
    while (client.IsConnected())
    {
        signal.waitForSignal();
        reader.processMsgs();
    }
}) { IsBackground = true };
readerThread.Start();

bool ready = wrapper.Ready.Wait(TimeSpan.FromSeconds(30));
if (ready && client.IsConnected())
{
    Console.ForegroundColor = ConsoleColor.Green;
    Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] SUCCESS: TWS completed nextValidId handshake. Next order id: {wrapper.NextOrderId}.");
    Console.ResetColor();
    client.reqCurrentTime();
    client.reqManagedAccts();
}
else
{
    Console.ForegroundColor = ConsoleColor.Red;
    Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] FAILED: socket opened but nextValidId did not arrive within 30s. Connected={client.IsConnected()}.");
    Console.ResetColor();
}

Console.WriteLine("Leave this window open to watch callbacks, or press Enter to disconnect and close.");
Console.ReadLine();
if (client.IsConnected()) client.eDisconnect();

sealed class ProbeWrapper : DefaultEWrapper
{
    public ManualResetEventSlim Ready { get; } = new(false);
    public int NextOrderId { get; private set; }

    public override void nextValidId(int orderId)
    {
        NextOrderId = orderId;
        Ready.Set();
        Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] nextValidId: {orderId}");
    }

    public override void managedAccounts(string accountsList)
        => Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] managedAccounts: {accountsList}");

    public override void currentTime(long time)
        => Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] currentTime: {DateTimeOffset.FromUnixTimeSeconds(time):u}");

    public override void error(int id, long errorTime, int errorCode, string errorMsg, string advancedOrderRejectJson)
    {
        Console.ForegroundColor = errorCode is 2104 or 2106 or 2107 or 2158 or 2119 ? ConsoleColor.DarkGray : ConsoleColor.Red;
        Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] IBKR {(Console.ForegroundColor == ConsoleColor.Red ? "ERROR" : "INFO")} id={id} code={errorCode}: {errorMsg}");
        if (!string.IsNullOrWhiteSpace(advancedOrderRejectJson)) Console.WriteLine($"  reject: {advancedOrderRejectJson}");
        Console.ResetColor();
    }

    public override void error(Exception e)
    {
        Console.ForegroundColor = ConsoleColor.Red;
        Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] IBKR EXCEPTION: {e.Message}");
        Console.ResetColor();
    }

    public override void error(string str)
    {
        Console.ForegroundColor = ConsoleColor.Red;
        Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] IBKR ERROR: {str}");
        Console.ResetColor();
    }

    public override void connectionClosed()
    {
        Console.ForegroundColor = ConsoleColor.Yellow;
        Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] connectionClosed callback received.");
        Console.ResetColor();
    }
}