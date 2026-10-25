using System.ComponentModel;
using FusionNet.Core.Abstractions;
using FusionNet.Core.Models;
using System.ServiceProcess;

namespace FusionNet.Windows.System;

/// <summary>
/// Thin, non-throwing wrapper over <see cref="ServiceController"/>. Stops and starts the
/// download/update services Fusion Mode pauses, with timeouts because Windows sometimes
/// restarts a stopped service on its own - the coordinator reconciles that every 2 seconds.
/// </summary>
public sealed class WindowsServiceController : IWindowsServiceController
{
    private const int ErrorServiceDoesNotExist = 1060;
    private const int ErrorAccessDenied = 5;

    public ServiceStateInfo GetState(ServiceDefinition service)
    {
        ArgumentNullException.ThrowIfNull(service);

        try
        {
            using var controller = new ServiceController(service.ServiceName);
            var status = controller.Status;
            return new ServiceStateInfo(
                service.Id,
                service.NameEn,
                Map(status),
                CanStop: status is ServiceControllerStatus.Running or ServiceControllerStatus.Paused);
        }
        catch (InvalidOperationException ex) when (IsMissingService(ex))
        {
            return new ServiceStateInfo(service.Id, service.NameEn, ServicePauseState.NotFound, false, "Service is not installed.");
        }
        catch (InvalidOperationException ex)
        {
            return new ServiceStateInfo(service.Id, service.NameEn, ServicePauseState.Unknown, false, ex.Message);
        }
        catch (Exception ex) when (ex is Win32Exception or UnauthorizedAccessException)
        {
            return new ServiceStateInfo(service.Id, service.NameEn, ServicePauseState.Unknown, false, ex.Message);
        }
    }

    public OperationResult Stop(ServiceDefinition service, TimeSpan timeout)
    {
        ArgumentNullException.ThrowIfNull(service);

        try
        {
            using var controller = new ServiceController(service.ServiceName);
            ServiceControllerStatus status;
            try
            {
                status = controller.Status;
            }
            catch (InvalidOperationException ex) when (IsMissingService(ex))
            {
                // Not installed on this Windows edition: nothing to pause, so this is a success.
                return OperationResult.Ok($"{service.NameEn} is not installed; nothing to stop.");
            }

            if (status == ServiceControllerStatus.Stopped)
                return OperationResult.Ok($"{service.NameEn} is already stopped.");

            if (status is ServiceControllerStatus.StopPending)
            {
                return WaitForStop(controller, service, timeout);
            }

            controller.Stop();
            return WaitForStop(controller, service, timeout);
        }
        catch (Exception ex) when (IsAccessDenied(ex))
        {
            return OperationResult.NotElevated("Stopping " + service.NameEn);
        }
        catch (InvalidOperationException ex) when (IsMissingService(ex))
        {
            return OperationResult.Ok($"{service.NameEn} is not installed; nothing to stop.");
        }
        catch (Exception ex) when (ex is global::System.TimeoutException)
        {
            return OperationResult.Fail($"{service.NameEn} did not stop within {Seconds(timeout)}s. Windows may restart it during a match.");
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException or UnauthorizedAccessException or SystemException)
        {
            return OperationResult.Fail($"Could not stop {service.NameEn}: {ex.Message}");
        }
    }

    public OperationResult Start(ServiceDefinition service, TimeSpan timeout)
    {
        ArgumentNullException.ThrowIfNull(service);

        try
        {
            using var controller = new ServiceController(service.ServiceName);
            ServiceControllerStatus status;
            try
            {
                status = controller.Status;
            }
            catch (InvalidOperationException ex) when (IsMissingService(ex))
            {
                return OperationResult.Ok($"{service.NameEn} is not installed; nothing to start.");
            }

            if (status == ServiceControllerStatus.Running)
                return OperationResult.Ok($"{service.NameEn} is already running.");

            if (status == ServiceControllerStatus.StartPending)
            {
                return WaitForStart(controller, service, timeout);
            }

            controller.Start();
            // Services can be configured as "Automatic (Delayed Start)"; give Windows the full timeout.
            var effective = timeout <= TimeSpan.Zero ? TimeSpan.FromSeconds(15) : timeout;
            controller.WaitForStatus(ServiceControllerStatus.Running, effective);
            return OperationResult.Ok($"{service.NameEn} started again.");
        }
        catch (Exception ex) when (IsAccessDenied(ex))
        {
            return OperationResult.NotElevated("Starting " + service.NameEn);
        }
        catch (InvalidOperationException ex) when (IsMissingService(ex))
        {
            return OperationResult.Ok($"{service.NameEn} is not installed; nothing to start.");
        }
        catch (Exception ex) when (ex is global::System.TimeoutException)
        {
            return OperationResult.Fail($"{service.NameEn} did not start within {Seconds(timeout)}s.");
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException or UnauthorizedAccessException or SystemException)
        {
            return OperationResult.Fail($"Could not start {service.NameEn}: {ex.Message}");
        }
    }

    private static OperationResult WaitForStop(ServiceController controller, ServiceDefinition service, TimeSpan timeout)
    {
        var effective = timeout <= TimeSpan.Zero ? TimeSpan.FromSeconds(15) : timeout;
        try
        {
            controller.WaitForStatus(ServiceControllerStatus.Stopped, effective);
            return OperationResult.Ok($"{service.NameEn} stopped.");
        }
        catch (global::System.TimeoutException)
        {
            return OperationResult.Fail($"{service.NameEn} did not stop within {Seconds(timeout)}s. Windows may restart it during a match.");
        }
    }

    private static OperationResult WaitForStart(ServiceController controller, ServiceDefinition service, TimeSpan timeout)
    {
        var effective = timeout <= TimeSpan.Zero ? TimeSpan.FromSeconds(15) : timeout;
        try
        {
            controller.WaitForStatus(ServiceControllerStatus.Running, effective);
            return OperationResult.Ok($"{service.NameEn} started again.");
        }
        catch (global::System.TimeoutException)
        {
            return OperationResult.Fail($"{service.NameEn} did not start within {Seconds(timeout)}s.");
        }
    }

    private static ServicePauseState Map(ServiceControllerStatus status) => status switch
    {
        ServiceControllerStatus.Running => ServicePauseState.Running,
        ServiceControllerStatus.Stopped => ServicePauseState.Stopped,
        ServiceControllerStatus.StartPending => ServicePauseState.StartPending,
        ServiceControllerStatus.StopPending => ServicePauseState.StopPending,
        ServiceControllerStatus.Paused or ServiceControllerStatus.PausePending or ServiceControllerStatus.ContinuePending =>
            ServicePauseState.Paused,
        _ => ServicePauseState.Unknown
    };

    private static bool IsMissingService(InvalidOperationException exception) =>
        FindWin32Error(exception) == ErrorServiceDoesNotExist;

    private static bool IsAccessDenied(Exception exception) =>
        exception is UnauthorizedAccessException ||
        FindWin32Error(exception) == ErrorAccessDenied;

    private static int? FindWin32Error(Exception exception)
    {
        for (var current = exception; current is not null; current = current.InnerException)
        {
            if (current is Win32Exception win32) return win32.NativeErrorCode;
        }

        return null;
    }

    private static string Seconds(TimeSpan timeout) => (timeout <= TimeSpan.Zero ? 15 : timeout.TotalSeconds).ToString("0");
}
