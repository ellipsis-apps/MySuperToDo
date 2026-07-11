using Microsoft.AspNetCore.Components;

namespace MySuperToDo.Pages.User;

public partial class RelaySetupDialog
{
    [Parameter] public string Username { get; set; } = string.Empty;
    [Parameter] public string? UserId { get; set; }

    private string _relayServersText = string.Empty;
    private string? _errorMessage;

    protected override Task OnInitializedAsync()
    {
        _relayServersText = string.Join(Environment.NewLine, GunDb.PeerUrls);
        return Task.CompletedTask;
    }

    private void Cancel() => DialogService.Close(false);
}
