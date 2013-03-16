Imports NetworkCommsDotNet
Imports NLog
Imports NLog.Config
Imports NLog.Targets

Module Program

    Sub Main()
        Try
            Console.SetBufferSize(120, 200)
            Console.SetWindowSize(120, 25)
        Catch ex As NotImplementedException
        End Try

        Thread.CurrentThread.Name = "MainThread"

        Console.WriteLine("Initiating NetworkCommsDotNet examples." + Environment.NewLine)

        'Ask user if they want to enable comms logging
        SelectLogging()

        'All we do here is let the user choice a specific example
        Console.WriteLine("Please enter an example number:")

        'Print out the available examples
        Dim totalNumberOfExamples As Integer = 1
        Console.WriteLine("1 - Basic - Message Send (Only 11 lines!)")

        'Get the user choice
        Console.WriteLine("")
        Dim selectedExample As Integer
        While True
            Dim parseSucces As Boolean = Integer.TryParse(Console.ReadKey().KeyChar.ToString(), selectedExample)
            If parseSucces And selectedExample <= totalNumberOfExamples Then Exit While
            Console.WriteLine(Environment.NewLine + "Invalid example choice. Please try again.")
        End While

        'Clear all input so that each example can do it's own thing
        Console.Clear()

        'Run the selected example
        Try
            Select Case selectedExample
                Case 1
                    BasicSend.RunExample()
                Case Else
                    Console.WriteLine("Selected an invalid example number. Please restart and try again.")
            End Select
        Catch ex As Exception
            NetworkComms.LogError(ex, "ExampleError")
            NetworkComms.Shutdown()
            Console.WriteLine(ex.ToString())
        End Try

        'When we are done we give the user a chance to see all output
        Console.WriteLine(Environment.NewLine + Environment.NewLine + "Example has completed. Please press any key to close.")
        Console.ReadKey(True)
    End Sub

    Sub SelectLogging()
        ''If the user wants to enable logging 
        Console.WriteLine("To enable comms logging press 'y'. To leave logging disabled and continue press any other key." + Environment.NewLine)

        If (Console.ReadKey(True).key = ConsoleKey.Y) Then
            ''''''''''''''''''''''''''''''''''''''''''''''''''''''''''''''''''''''
            '''' SIMPLE CONSOLE ONLY LOGGING
            '''' See http:''nlog-project.org' for more information
            '''' Requires that the file NLog.dll is present 
            ''''''''''''''''''''''''''''''''''''''''''''''''''''''''''''''''''''''
            'Dim logConfig = New LoggingConfiguration()
            '
            'Dim consoleTarget = New ConsoleTarget()
            'consoleTarget.Layout = "${date:format=HH\:mm\:ss} - ${message}"
            '
            'logConfig.AddTarget("console", consoleTarget)
            '
            'logConfig.LoggingRules.Add(New LoggingRule("*", LogLevel.Debug, consoleTarget))
            'NetworkComms.EnableLogging(logConfig)

            ''''''''''''''''''''''''''''''''''''''''''''''''''''''''''''''''''''''
            '''' THE FOLLOWING CONFIG LOGS TO BOTH A FILE AND CONSOLE
            '''' See http:''nlog-project.org' for more information
            '''' Requires that the file NLog.dll is present 
            ''''''''''''''''''''''''''''''''''''''''''''''''''''''''''''''''''''''
            Dim logConfig = New LoggingConfiguration()
            Dim fileTarget = New FileTarget()
            fileTarget.FileName = "${basedir}/ExamplesConsoleLog_" + NetworkComms.NetworkIdentifier.ToString() + ".txt"
            fileTarget.Layout = "${date:format=HH\:mm\:ss} [${threadid} - ${level}] - ${message}"
            Dim consoleTarget = New ConsoleTarget()
            consoleTarget.Layout = "${date:format=HH\:mm\:ss} - ${message}"

            logConfig.AddTarget("file", fileTarget)
            logConfig.AddTarget("console", consoleTarget)

            logConfig.LoggingRules.Add(New LoggingRule("*", LogLevel.Trace, fileTarget))
            logConfig.LoggingRules.Add(New LoggingRule("*", LogLevel.Debug, consoleTarget))
            NetworkComms.EnableLogging(logConfig)

            ''We can write to our logger from an external program as well
            NetworkComms.Logger.Info("NetworkCommsDotNet logging enabled. DEBUG level ouput and above directed to console. ALL output also directed to log file, ExamplesConsoleLog_" + NetworkComms.NetworkIdentifier.ToString() + ".txt." + Environment.NewLine)
        End If
    End Sub

End Module
