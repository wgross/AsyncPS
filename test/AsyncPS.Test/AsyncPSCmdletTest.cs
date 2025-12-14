using System.Diagnostics;
using System.Management.Automation;
using System.Management.Automation.Runspaces;
using static Xunit.Assert;

namespace AsyncPS.Test;

public class AsyncPSCmdletTest
{
    private readonly PowerShell powershell;

    public AsyncPSCmdletTest()
    {
        var sessionState = InitialSessionState.CreateDefault();
        sessionState.Commands.Add(new SessionStateCmdletEntry("write-object_from_async_code", typeof(write_object_from_async_code_cmdlet), helpFileName: null));
        sessionState.Commands.Add(new SessionStateCmdletEntry("write-exception_from_async_code", typeof(write_exception_from_async_code_cmdlet), helpFileName: null));
        sessionState.Commands.Add(new SessionStateCmdletEntry("stop-async_code", typeof(stop_async_code_cmdlet), helpFileName: null));
        this.powershell = PowerShell.Create(sessionState);
    }

    #region test cmdlets

    [Cmdlet(VerbsCommunications.Write, "object_from_async_code")]
    [OutputType(typeof(string))]
    private sealed class write_object_from_async_code_cmdlet : AsyncPSCmdlet
    {
        protected override async Task ProcessRecordAsync(CancellationToken cancellationToken)
        {
            await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken);

            this.Dispatch(() => this.WriteObject("1"));

            await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken);

            this.Dispatch(() => this.WriteObject("2"));
        }
    }

    [Cmdlet(VerbsCommunications.Write, "exception_from_async_code")]
    [OutputType(typeof(string))]
    private sealed class write_exception_from_async_code_cmdlet : AsyncPSCmdlet
    {
        protected override async Task ProcessRecordAsync(CancellationToken cancellationToken)
        {
            await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken);

            throw new Exception("fail");
        }
    }

    [Cmdlet(VerbsLifecycle.Stop, "async_code")]
    [OutputType(typeof(string))]
    private sealed class stop_async_code_cmdlet : AsyncPSCmdlet
    {
        protected override async Task ProcessRecordAsync(CancellationToken cancellationToken)
        {
            cancellationToken.Register(() => this.Dispatch(() => this.WriteObject("stopped")));
            await Task.Delay(TimeSpan.FromSeconds(10), cancellationToken);
        }
    }

    #endregion

    [Fact]
    public void Write_object_from_async_code()
    {
        // ACT
        var result = this.powershell.AddCommand("write-object_from_async_code").Invoke();

        // ASSERT
        Equal(["1", "2"], result.Select(o => o.BaseObject));
    }

    [Fact]
    public void Write_exception_from_async_code()
    {
        // ACT
        var result = this.powershell.AddCommand("write-exception_from_async_code").Invoke();

        // ASSERT
        IsType<Exception>(this.powershell.Streams.Error.Single().Exception);
        Equal("fail", this.powershell.Streams.Error.Single().Exception.Message);
    }

    [Fact]
    public async Task Stop_async_code()
    {
        // ARRANGE
        Stopwatch sw = new();
        sw.Start();

        // cmdlet runs for 10s
        var psTask = this.powershell.AddCommand("stop-async_code").InvokeAsync();

        await Task.Delay(TimeSpan.FromSeconds(1)); // let cmdlet start processing
        
        // ACT
        this.powershell.Stop();
        sw.Stop();

        // ASSERT
        await ThrowsAsync<PipelineStoppedException>(async () => await psTask);

        // stop immediately <<10s
        True(sw.Elapsed < TimeSpan.FromSeconds(2));
    }
}