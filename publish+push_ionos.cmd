@echo off
echo ================================
echo Publishing MySuperToDo Blazor WASM
echo ================================

dotnet publish ".\MySuperToDo\MySuperToDo.csproj" -c Release -o ".\publish" -p:BaseHref="/MySuperToDo/"
if %ERRORLEVEL% neq 0 (
    echo Publish failed.
    goto failed
)

echo Publish complete.


echo ================================
echo Clearing REMOTE folder
echo ================================

set REMOTEUSER=silverfox1948
set REMOTEHOST=ionos
set REMOTETARGET=/home/silverfox1948/myapps/mysupertodo

ssh %REMOTEHOST% "sudo rm -rf %REMOTETARGET%/*"
if %ERRORLEVEL% neq 0 (
    echo Failed to clear REMOTEHOST folder.
    goto failed
)

echo REMOTEHOST folder cleared.


echo ================================
echo Copying new build to REMOTEHOST
echo ================================

scp -r ".\publish\wwwroot\*" %REMOTEUSER%@%REMOTEHOST%:%REMOTETARGET%
if %ERRORLEVEL% neq 0 (
    echo Copy failed.
    goto failed
)

echo ================================
echo Replacing appsettings.json on Pi
echo ================================

scp ".\publish\wwwroot\appsettings.json.ignore" %REMOTEUSER%@%REMOTEHOST%:%REMOTETARGET%/appsettings.json
if %ERRORLEVEL% neq 0 (
    echo Failed to replace appsettings.json.
    goto failed
)

echo ================================
echo appsettings.json replaced.
echo ================================
echo Copy complete.
echo ================================

echo ================================
echo Restarting Caddy on Pi
echo ================================

ssh %REMOTEHOST% "sudo systemctl restart caddy"
if %ERRORLEVEL% neq 0 (
    echo Failed to restart Caddy.
    goto failed
)

echo Caddy restarted successfully.
echo ================================
echo Deployment complete.
echo ================================

echo.
echo Press any key to exit...
pause >nul
exit /b 0


:failed
echo.
echo Deployment failed.
echo Press any key to exit...
pause >nul
exit /b 1
