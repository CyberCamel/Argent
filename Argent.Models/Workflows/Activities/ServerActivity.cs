namespace Argent.Models.Workflows.Activities;

public abstract class ServerActivity : Activity
{
    public int MaxRetries { get; set; }
}
