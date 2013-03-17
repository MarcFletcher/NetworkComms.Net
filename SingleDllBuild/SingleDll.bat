call .\SingleDll_Net20.bat %1
call .\SingleDll_Net35.bat %1
call .\SingleDll_Net40.bat %1

REM It was not possible to get a working merge for WP8 at the time of the current release
REM we will continue to investigate
REM call .\SingleDll_WP8.bat %1

RMDIR /s /q ".\%1"