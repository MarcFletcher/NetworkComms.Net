' Copyright 2009-2014 Marc Fletcher, Matthew Dean
' 
' This program is free software: you can redistribute it and/or modify
' it under the terms of the GNU General Public License as published by
' the Free Software Foundation, either version 3 of the License, or
' (at your option) any later version.
' 
' This program is distributed in the hope that it will be useful,
' but WITHOUT ANY WARRANTY; without even the implied warranty of
' MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
' GNU General Public License for more details.
' 
' You should have received a copy of the GNU General Public License
' along with this program.  If not, see <http://www.gnu.org/licenses/>.
' 
' A commercial license of this software can also be purchased. 
' Please see <http://www.networkcomms.net/licensing/> for details.

Imports NetworkCommsDotNet
Imports NetworkCommsDotNet.Tools

Module ExampleHelper
    Private lastServerIPEndPoint As IPEndPoint = Nothing

    Function GetServerDetails() As ConnectionInfo
        If Not IsNothing(lastServerIPEndPoint) Then
            Console.WriteLine("Please enter the destination IP and port. To reuse '{0}:{1}' use r:", lastServerIPEndPoint.Address, lastServerIPEndPoint.Port)
        Else
            Console.WriteLine("Please enter the destination IP address and port, e.g. '192.168.0.1:10000':")
        End If

        While True
            Try
                'Parse the provided information
                Dim userEnteredStr As String = Console.ReadLine()

                If userEnteredStr.Trim() = "r" And Not IsNothing(lastServerIPEndPoint) Then
                    Exit While
                Else
                    lastServerIPEndPoint = IPTools.ParseEndPointFromString(userEnteredStr)
                    Exit While
                End If
            Catch ex As Exception
                Console.WriteLine("Unable to determine host IP address and port. Check format and try again:")
            End Try
        End While

        Return New ConnectionInfo(lastServerIPEndPoint)
    End Function
End Module
