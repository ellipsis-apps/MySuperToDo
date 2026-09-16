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
echo Clearing Pi folder
echo ================================

set PIUSER=silverfox1948
set PIHOST=10.0.0.113
set PITARGET=/usr/share/caddy/mysupertodo

ssh %PIUSER%@%PIHOST% "sudo rm -rf %PITARGET%/*"
if %ERRORLEVEL% neq 0 (
    echo Failed to clear Pi folder.
    goto failed
)

echo Pi folder cleared.


echo ================================
echo Copying new build to Pi
echo ================================

scp -r ".\publish\wwwroot\*" %PIUSER%@%PIHOST%:%PITARGET%
if %ERRORLEVEL% neq 0 (
    echo Copy failed.
    goto failed
)

echo ================================
echo Replacing appsettings.json on Pi
echo ================================

scp ".\publish\wwwroot\appsettings.json.ignore" %PIUSER%@%PIHOST%:%PITARGET%/appsettings.json
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

ssh %PIUSER%@%PIHOST% "sudo systemctl restart caddy"
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
