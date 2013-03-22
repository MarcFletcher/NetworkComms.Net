call .\SingleDll_Net20.bat %1
call .\SingleDll_Net35.bat %1
call .\SingleDll_Net40.bat %1

call .\SingleDll_Xamarin.iOS.bat %1
call .\SingleDll_Xamarin.Android.bat %1

call .\SingleDll_WP8.bat %1

RMDIR /s /q ".\%1"