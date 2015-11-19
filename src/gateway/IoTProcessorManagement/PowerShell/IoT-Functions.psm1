<#
// ------------------------------------------------------------
//  Copyright (c) Microsoft Corporation.  All rights reserved.
//  Licensed under the MIT License (MIT). See License.txt in the repo root for license information.
// ------------------------------------------------------------
#>
$_default_ServiceFabricConnection = "localhost:19000"
$_default_MgmtSvcName= "fabric:/IoTProcessorManagementApp/ProcessorManagementService";




Function Ensure-IoTManagementApiEndPoint
(
	[string]
	$EndPoint
)
{
	$newEndPoint = $EndPoint 
	
	if([string]::IsNullOrEmpty($newEndPoint))
	{
	
		$newEndPoint = Get-IoTManagementApiEndPoint
	}
	$newEndPoint
}



Function Get-IoTProcessorName
(
	$IoTProcessor
)
{
		if ($IoTProcessor.GetType()  -Eq [String])
		{
			if([string]::IsNullOrEmpty($IoTProcessor))
			{
				Write-Error "Processor paramter is empty"
				return
			}
			$IoTProcessorName = $IoTProcessor
	    }
		else
		{

			<#									 
			if($IoTProcessor -isnot [IoTProcessorManagement.Clients.Processor])
			{
				Write-Error "Processor paramter is not of type [IoTProcessManagement.Clients.Processor]"
				exit
			}
			#>
			$IoTProcessorName = $IoTProcessor.Name
		}

	$IoTProcessorName 
}


Function Get-IoTManagementApiEndPoint
{
 <#
  .Synopsis
   Gets the endpoint address for IoT Management Service.
 #>
[CmdletBinding()]
param
(
      [Parameter(Mandatory = $false,Position = 0,valueFromPipeline=$false)]
      [string]
      $ServiceFabricEndPoint = $_default_ServiceFabricConnection,
      [Parameter(Mandatory = $false,Position = 1,valueFromPipeline=$false)]
      [string]
      $IoTManagementSvcName = $_default_MgmtSvcName
) 

	$MgmtEndpoint = [IoTProcessorManagement.Functions]::GetManagementApiEndPoint($ServiceFabricEndPoint, $IoTManagementSvcName)
	$MgmtEndpoint
}

Function Add-IoTProcessor
{
 <#
  .Synopsis
   Adds a processor
   A processor (IoTProcessorManagement.Clients.Processor) object is needed 
   if ManagementEndPoint is not given IoTManagementApiEndPoint will be called using defaults:
	Cluster: localhost:19000
	Management API ServiceName: "fabric:/IoTProcessorManagementApp/ProcessorManagementService"
 #>
[CmdletBinding()]
param
(
	  [Parameter(Mandatory = $true,Position = 0,valueFromPipeline=$true)]
      [IoTProcessorManagement.Clients.Processor]
      $IoTProcessor,

	  
	  [Parameter(Mandatory = $false,Position = 1,valueFromPipeline=$false)]
      [string]
      $ManagementEndPoint = ""
) 
	if($IoTProcessor -eq $null)
	{
		throw "IoTProcessor is null"
	}
	$ManagementEndPoint = Ensure-IoTManagementApiEndPoint -EndPoint $ManagementEndPoint	
	$ret = [IoTProcessorManagement.Functions]::AddProcessor($ManagementEndPoint, $IoTProcessor)	
	$ret 
}

Function Import-IoTProcessor
{
 <#
  .Synopsis
   Adds or updates (via the update switch) a processor via importing it from a json file
   if ManagementEndPoint is not given IoTManagementApiEndPoint will be called using defaults:
	Cluster: localhost:19000
	Management API ServiceName: "fabric:/IoTProcessorManagementApp/ProcessorManagementService"
 #>
[CmdletBinding()]
param
(
	  [Parameter(Mandatory = $true,Position = 0,valueFromPipeline=$true)]
      [ValidateScript({Test-Path $_ -PathType 'leaf'})]  
      [string]
      $FilePath = "",

	  
	  [Parameter(Mandatory = $false,Position = 1,valueFromPipeline=$false)]
      #[ValidatePattern("/^(http?:\/\/)?([\da-z\.-]+)\.([a-z\.]{2,6})([\/\w \.-]*)*\/?$/")]
	  [string]
      $ManagementEndPoint = "",

	 [switch]
	 $Update
)
    $ManagementEndPoint = Ensure-IoTManagementApiEndPoint -EndPoint $ManagementEndPoint
	$content = (Get-Content $FilePath) 
 
	if($Update -eq $true)
	{
		$ret = [IoTProcessorManagement.Functions]::UpdateProcessor($ManagementEndPoint, $content)	
		$ret 
	}
	else
	{
		$ret = [IoTProcessorManagement.Functions]::AddProcessor($ManagementEndPoint, $content)	
		$ret 
	}
}



Function Remove-IoTProcessor
{
 <#
  .Synopsis
  Removes a processor
   Either a processor name (string) or a processor (IoTProcessorManagement.Clients.Processor) object is needed 
   if ManagementEndPoint is not given IoTManagementApiEndPoint will be called using defaults:
	Cluster: localhost:19000
	Management API ServiceName: "fabric:/IoTProcessorManagementApp/ProcessorManagementService"
 #>
[CmdletBinding()]
param
(
	 [Parameter(Mandatory = $true,Position = 0,valueFromPipeline=$true)]
	 [string]
      $IoTProcessor ,
	  [Parameter(Mandatory = $false,Position = 1,valueFromPipeline=$false)]
      [string]
      $ManagementEndPoint 
      
) 
	$ManagementEndPoint = Ensure-IoTManagementApiEndPoint -EndPoint $ManagementEndPoint
	$IoTProcessorName = Get-IoTProcessorName -IoTProcessor $IoTProcessor


	$ret = [IoTProcessorManagement.Functions]::DeleteProcessor($ManagementEndPoint, $IoTProcessorName )	
	$ret 
}

Function Get-IoTProcessor
{
 <#
  .Synopsis
   Gets One or all processors
   Either a processor name (string) or a processor (IoTProcessorManagement.Clients.Processor) object is needed 
   if ManagementEndPoint is not given IoTManagementApiEndPoint will be called using defaults:
	Cluster: localhost:19000
	Management API ServiceName: "fabric:/IoTProcessorManagementApp/ProcessorManagementService"
 #>
[CmdletBinding()]
param
(
	  [Parameter(Mandatory = $false,Position = 0,valueFromPipeline=$true)]
      $IoTProcessor ,
	  [Parameter(Mandatory = $false,Position = 1,valueFromPipeline=$false)]
	  [string]
      $ManagementEndPoint = ""
      
) 
	
	$ManagementEndPoint = Ensure-IoTManagementApiEndPoint -EndPoint $ManagementEndPoint
	
	if ($IoTProcessor -eq $null)
	{
		$ret = [IoTProcessorManagement.Functions]::GetAllProcesseros($ManagementEndPoint)	
		$ret 
	}
	else
	{
		$IoTProcessorName = Get-IoTProcessorName -IoTProcessor $IoTProcessor
		$ret =[IoTProcessorManagement.Functions]::GetPrcossor($ManagementEndPoint, $IoTProcessorName)
		$ret 
	}

}



Function Suspend-IoTProcessor
{
 <#
  .Synopsis
   Suspends a processor
   Either a processor name (string) or a processor (IoTProcessorManagement.Clients.Processor) object is needed 
   if ManagementEndPoint is not given IoTManagementApiEndPoint will be called using defaults:
	Cluster: localhost:19000
	Management API ServiceName: "fabric:/IoTProcessorManagementApp/ProcessorManagementService"
 #>
[CmdletBinding()]
param
(
	  [Parameter(Mandatory = $true,Position = 0,valueFromPipeline=$true)]
      $IoTProcessor,
		
	  [Parameter(Mandatory = $false,Position = 1,valueFromPipeline=$false)]
      [string]
      $ManagementEndPoint 
    
) 
	$ManagementEndPoint = Ensure-IoTManagementApiEndPoint -EndPoint $ManagementEndPoint
	$IoTProcessorName = Get-IoTProcessorName -IoTProcessor $IoTProcessor
	$ret = [IoTProcessorManagement.Functions]::PauseProcessor($ManagementEndPoint, $IoTProcessorName)	
	$ret 
}

Function Resume-IoTProcessor
{
 <#
  .Synopsis
   Resumes a processor
   Either a processor name (string) or a processor (IoTProcessorManagement.Clients.Processor) object is needed 
   if ManagementEndPoint is not given IoTManagementApiEndPoint will be called using defaults:
	Cluster: localhost:19000
	Management API ServiceName: "fabric:/IoTProcessorManagementApp/ProcessorManagementService"
 #>
[CmdletBinding()]
param
(
      [Parameter(Mandatory = $true,Position = 0,valueFromPipeline=$true)]
      $IoTProcessor,

	  [Parameter(Mandatory = $false,Position = 1,valueFromPipeline=$false)]
      [string]
      $ManagementEndPoint 
)
	$ManagementEndPoint = Ensure-IoTManagementApiEndPoint -EndPoint $ManagementEndPoint
	$IoTProcessorName = Get-IoTProcessorName -IoTProcessor $IoTProcessor
 
	$ret = [IoTProcessorManagement.Functions]::ResumeProcessor($ManagementEndPoint, $IoTProcessorName )	
	$ret
}

Function Stop-IoTProcessor
{
 <#
  .Synopsis
   Stops for processor or (drain and stop) a processor via the drain switch
   Either a processor name (string) or a processor (IoTProcessorManagement.Clients.Processor) object is needed 
   if ManagementEndPoint is not given IoTManagementApiEndPoint will be called using defaults:
	Cluster: localhost:19000
	Management API ServiceName: "fabric:/IoTProcessorManagementApp/ProcessorManagementService"
 #>
[CmdletBinding()]
param
(
	  [Parameter(Mandatory = $true,Position = 0,valueFromPipeline=$true)]
      $IoTProcessor,

	
	  [Parameter(Mandatory = $false,Position = 1,valueFromPipeline=$false)]
      [switch]
      $drain,


	  [Parameter(Mandatory = $false,Position = 2,valueFromPipeline=$false)]
      [string]
      $ManagementEndPoint 
) 
	$ManagementEndPoint = Ensure-IoTManagementApiEndPoint -EndPoint $ManagementEndPoint
	$IoTProcessorName = Get-IoTProcessorName -IoTProcessor $IoTProcessor
 
	if($drain -eq $true)
	{
		$ret = [IoTProcessorManagement.Functions]::DrainStopProcessor($ManagementEndPoint, $IoTProcessorName)	
	}
	else
	{
		$ret = [IoTProcessorManagement.Functions]::StopProcessor($ManagementEndPoint, $IoTProcessorName)	
	}

	$ret
}



Function Update-IoTProcessor
{
 <#
  .Synopsis
   Updates a processor
   A processor (IoTProcessorManagement.Clients.Processor) object is needed 
   if ManagementEndPoint is not given IoTManagementApiEndPoint will be called using defaults:
	Cluster: localhost:19000
	Management API ServiceName: "fabric:/IoTProcessorManagementApp/ProcessorManagementService" 
 #>
[CmdletBinding()]
param
(
	  [Parameter(Mandatory = $true,Position = 0,valueFromPipeline=$true)]
	  [IoTProcessorManagement.Clients.Processor]
      $IoTProcessor,

	  [Parameter(Mandatory = $false,Position = 1,valueFromPipeline=$false)]
      [string]
      $ManagementEndPoint 
      
	  
)
	if($IoTProcessor -eq $null)
	{
		throw "IoTProcessor is null"
	}

	$ManagementEndPoint = Ensure-IoTManagementApiEndPoint -EndPoint $ManagementEndPoint 
	$ret = [IoTProcessorManagement.Functions]::UpdateProcessor($ManagementEndPoint, $IoTProcessor)	
	$ret 
}




Function Get-IoTProcessorRuntimeStatus
{
 <#
  .Synopsis
   Gets a the runtime telemetry for a processor
   Either a processor name (string) or a processor (IoTProcessorManagement.Clients.Processor) object is needed 
   if ManagementEndPoint is not given IoTManagementApiEndPoint will be called using defaults:
	Cluster: localhost:19000
	Management API ServiceName: "fabric:/IoTProcessorManagementApp/ProcessorManagementService"
   Rolle up switch will aggregate the numbers a cross all partitions
 #>
[CmdletBinding()]
param
(
	  [Parameter(Mandatory = $true,Position = 0,valueFromPipeline=$true)]
      $IoTProcessor,

	  [Parameter(Mandatory = $false,Position = 1,valueFromPipeline=$false)]
      [switch]
      $RollUp ,

	  [Parameter(Mandatory = $false,Position = 2,valueFromPipeline=$false)]
      [string]
      $ManagementEndPoint 
    


)
	$ManagementEndPoint = Ensure-IoTManagementApiEndPoint -EndPoint $ManagementEndPoint
	$IoTProcessorName = Get-IoTProcessorName -IoTProcessor $IoTProcessor
	
	$runtimeStatus = [IoTProcessorManagement.Functions]::GetDetailedProcessorStatus($ManagementEndPoint, $IoTProcessorName)	
	
	if($RollUp  -eq $true)
	{
		$count = $runtimeStatus.length
		
		$TotalPostedLastMinute    = 0;      
		$TotalProcessedLastMinute = 0;      
		$TotalPostedLastHour      = 0;      
		$TotalProcessedLastHour   = 0;      
		$AveragePostedPerMinLastHour  = 0;  
		$AverageProcessedPerMinLastHour = 0;
		$NumberOfBufferedItems = 0;
		$NumberOfActiveQueues = 0;
		$IsInErrorState  = $false;               
		$ErrorMessage    ="" ;      


		if($count-eq 0)
		{
			Write-Error "Runtime status for partitions in this processor are empty"
			return	
		}
		
		
		foreach ($RTStatus in $runtimeStatus) 
		{
			$TotalPostedLastMinute = $TotalPostedLastMinute + $RTStatus.TotalPostedLastMinute;
			$TotalProcessedLastMinute = $TotalProcessedLastMinute + $RTStatus.TotalProcessedLastMinute;      
			$TotalPostedLastHour      = $TotalPostedLastHour + $RTStatus.TotalPostedLastHour;      
			$TotalProcessedLastHour   = $TotalProcessedLastHour + $RTStatus.TotalProcessedLastHour;      
			$AveragePostedPerMinLastHour  = $AveragePostedPerMinLastHour + $RTStatus.AveragePostedPerMinLastHour;  
			$AverageProcessedPerMinLastHour = $AverageProcessedPerMinLastHour + $RTStatus.AverageProcessedPerMinLastHour;
			$NumberOfActiveQueues = $NumberOfActiveQueues + $RTStatus.NumberOfActiveQueues;
			$NumberOfBufferedItems = $NumberOfBufferedItems + $RTStatus.NumberOfBufferedItems;
			
			if($RTStatus.IsInErrorState -eq $true)
			{
				$IsInErrorState  = $true
			}

			$ErrorMessage = $ErrorMessage +  '`n' + $RTStatus.ErrorMessage
		}
			
		 

			$AveragePostedPerMinLastHour  = $AveragePostedPerMinLastHour / $count;  
			$AverageProcessedPerMinLastHour = $AverageProcessedPerMinLastHour / $count;

		

			# return as powershell object
				$properties = @{			
					'TotalPostedLastMinute' = $TotalPostedLastMinute ;
					'TotalProcessedLastMinute' = $TotalProcessedLastMinute ;      
					'TotalPostedLastHour'      = $TotalPostedLastHour ;      
					'TotalProcessedLastHour'   = $TotalProcessedLastHour ;      
					'AveragePostedPerMinLastHour'  = $AveragePostedPerMinLastHour ;  
					'AverageProcessedPerMinLastHour' = $AverageProcessedPerMinLastHour ;
					'NumberOfActiveQueues' = $NumberOfActiveQueues ;
					'NumberOfBufferedItems'	= $NumberOfBufferedItems;
				}

			$object = New-Object –TypeName PSObject –Prop $properties
			$object
	}
	else
	{
		$runtimeStatus
	}

}









<# IMPORT External Types #>

   Add-Type -Path '.\IoTProcessorManagement.Clients.dll'
   Add-Type -Path '.\IoTProcessorManagement.dll'

<# Modules Export #>   
   Export-ModuleMember -Function Get-IoTManagementApiEndPoint
   Export-ModuleMember -function Get-IoTProcessor
   Export-ModuleMember -function Import-IoTProcessor
   Export-ModuleMember -function Add-IoTProcessor
   Export-ModuleMember -function Remove-IoTProcessor
   
   Export-ModuleMember -function Update-IoTProcessor
   Export-ModuleMember -function Stop-IoTProcessor
   Export-ModuleMember -function Resume-IoTProcessor
   Export-ModuleMember -function Suspend-IoTProcessor
   Export-ModuleMember -function Get-IoTProcessorRuntimeStatus



<# REMOVE BEFORE DEPLOYMENT #>
Function Send-TestEvents
{
 <#
  .Synopsis
   Sends test event hub messages 
   A processor (IoTProcessorManagement.Clients.Processor) object is needed 

	You should consider using PS Jobs when calling this function
 #>
[CmdletBinding()]
param
(
	  [Parameter(Mandatory = $true,Position = 0,valueFromPipeline=$true)]
      [IoTProcessorManagement.Clients.Processor]
      $IoTProcessor,

	  
	  [Parameter(Mandatory = $false,Position = 1,valueFromPipeline=$false)]
      [int]
      $NumberOfMessages = 1,

	  [Parameter(Mandatory = $false,Position = 2,valueFromPipeline=$false)]
      [int]
      $NumberOfPublishers = 1

) 
	if($IoTProcessor -eq $null)
	{
		throw "IoTProcessor is null"
	}

	[IoTProcessorManagement.Functions]::SendProcessorTestMessages($IoTProcessor, $NumberOfMessages,$NumberOfPublishers)	
}
   

Export-ModuleMember -Function Send-TestEvents