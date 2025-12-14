# AsyncPS

AsyncPS provides a base class for C#-Cmdlets that have to invoke async code. 

Usually invoke in async code in power shell requires to wait in a blocking way for its completion:

```csharp
public class MyPsCmdlet: PSCmdlet
{
	override protected void ProcessRecord()
	{
		var returnValue = AsyncMethod().GetAwaiter().GetResult();
		
		// write to powershell pipe
		this.WriteObject(returnValue);
	}
}
```

Using the `AsyncPSCmdlet` instead of PowerShells `PSCmdlet` allows more seamless interaction with async code:
```csharp
public class MyPsCmdlet: AsyncPSCmdlet
{
	override protected async Task ProcessRecordAsync(CancellationToken cancellationToken)
	{
		var returnValue = await AsyncMethod(cancellationToken);
		
		// write to powershell pipe in main thread
		this.Dispatch(()=>this.WriteObject(returnValue));
	}
}
```

## Dispatching

Access to output methods is restricted to code running in the main thread. Background task arn't allowed to modify the UI managend bc the Powershell host. 

`AsyncPSCmdlet` provides a simple dispatching mechanism which runs the `Action` instances delivered by `AsyncPSCmdlet.Dispatch(Action)` with the thread that instantiated the Cmdlet. This can als be used form within the  async code:

```csharp
public class MyPsCmdlet: AsyncPSCmdlet
{
	override protected async Task ProcessRecordAsync(CancellationToken cancellationToken)
	{
		var returnValue = await AsyncMethod(cancellationToken);
		
		// write to powershell pipe in main thread
		this.Dispatch(() => this.WriteObject(returnValue));
	}
	
	private async Task AsyncMethod(CancellationToken cancellation)
	{
		var intermediateData = await ..
		
		this.Dispatch(() => this.WriteObject(intermediateData));
	}
}
```

## Cancellation

Stopping the PowerShell pipeline is either achieved by pressing Ctrl-C in the terminal or invoking `Powershell.Stop()` programmatically. in both cases the method `PSCmdlet.StopProcessing()`. 

`AsyncPSCmdlet` implements `StopProcessing` by cancelling a  `CancellationTokenSource`. The sources `CancellationToken` is given to the `ProcessRecordAsync(CancellationToken)` when called. the async code can now be cancelled in a proper  async way.