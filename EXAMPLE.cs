using System.Text;
using System.Threading.Channels;
using Spectre.Console;
using Spectre.Console.Rendering;

const string Project = "AOTfier";
const string Subline = "Dotnet tool to analyze projects for AOT compilation";
const int MaxMessages = 200;

var messages = new List<string>();
var sync = new object();

var console = AnsiConsole.Create(
    new AnsiConsoleSettings
    {
        Ansi = AnsiSupport.Yes,
        ColorSystem = ColorSystemSupport.TrueColor,
        Interactive = InteractionSupport.Yes,
    }
);

var events = Channel.CreateUnbounded<string>();
var cancellation = new CancellationTokenSource();

Console.CancelKeyPress += (_, e) =>
{
    e.Cancel = true;
    cancellation.Cancel();
};

var eventProducer = Task.Run(async () =>
{
    while (!cancellation.IsCancellationRequested)
    {
        await Task.Delay(1200, cancellation.Token);
        await events.Writer.WriteAsync($"{DateTime.Now:HH:mm:ss} Agent completed task", cancellation.Token);
    }
}, cancellation.Token);

var inputBuffer = new StringBuilder();

await console.Live(BuildDashboard(string.Empty))
    .StartAsync(async context =>
    {
        while (!cancellation.IsCancellationRequested)
        {
            while (events.Reader.TryRead(out var evt))
            {
                AddMessage(evt);
            }

            if (!Console.IsInputRedirected)
            {
                while (Console.KeyAvailable)
                {
                    var key = Console.ReadKey(intercept: true);

                    if (key.Key == ConsoleKey.Enter)
                    {
                        var command = inputBuffer.ToString().Trim();
                        inputBuffer.Clear();

                        if (!string.IsNullOrWhiteSpace(command))
                        {
                            if (string.Equals(command, "/quit", StringComparison.OrdinalIgnoreCase))
                            {
                                AddMessage($"{DateTime.Now:HH:mm:ss} Exit requested");
                                cancellation.Cancel();
                                break;
                            }

                            AddMessage($"{DateTime.Now:HH:mm:ss} Input: {command}");
                        }

                        continue;
                    }

                    if (key.Key == ConsoleKey.Backspace)
                    {
                        if (inputBuffer.Length > 0)
                        {
                            inputBuffer.Length--;
                        }

                        continue;
                    }

                    if (!char.IsControl(key.KeyChar))
                    {
                        inputBuffer.Append(key.KeyChar);
                    }
                }
            }

            context.UpdateTarget(BuildDashboard(inputBuffer.ToString()));

            try
            {
                await Task.Delay(80, cancellation.Token);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    });

cancellation.Cancel();

try
{
    await eventProducer;
}
catch (OperationCanceledException)
{
}

void AddMessage(string message)
{
    lock (sync)
    {
        messages.Add(message);

        if (messages.Count > MaxMessages)
        {
            messages.RemoveRange(0, messages.Count - MaxMessages);
        }
    }
}

IRenderable BuildDashboard(string activeInput)
{
    string[] snapshot;

    lock (sync)
    {
        snapshot = messages.ToArray();
    }

    var headerText = new Markup(
        $"[teal]Project[/]: [bold]{Markup.Escape(Project)}[/]\n         [italic]{Markup.Escape(Subline)}[/]"
    );

    var header = new Panel(headerText) { Height = 4 }
        .Header("- [teal bold]DevTeam[/] -", Justify.Center)
        .BorderColor(Color.Purple3)
        .Expand();

    var agentsPanel = new Panel("Watching agent events") { Height = 12 }
        .Header("- [teal bold]Agents[/] -")
        .BorderColor(Color.Purple3)
        .Expand();

    var tasksPanel = new Panel("Type commands below. Use /quit to exit.") { Height = 12 }
        .Header("- [teal bold]Tasks[/] -")
        .BorderColor(Color.Purple3)
        .Expand();

    var leftRow = new Rows(agentsPanel, tasksPanel);

    IRenderable progressContent = snapshot.Length == 0
        ? new Markup("[grey]No events yet[/]")
        : new Rows(snapshot.Select(m => (IRenderable)new Markup(Markup.Escape(m))).ToArray());

    var rightRow = new Panel(progressContent) { Height = 24 }
        .Header("- [teal bold]Progress[/] -")
        .BorderColor(Color.Purple3)
        .Expand();

    var body = new Columns(leftRow, rightRow);

    var prompt = new Panel(new Markup($"[bold aqua]>[/] {Markup.Escape(activeInput)}"))
        .Header("- [teal bold]Input[/] -")
        .BorderColor(Color.Purple3)
        .Expand();

    return new Rows(header, body, prompt);
}
