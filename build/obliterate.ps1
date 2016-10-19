<#
.SYNOPSIS 
Tear down everything.

.DESCRIPTION
Completely removes the IoT solution from a cluster.
 
.PARAMETER PublishProfileName
Name of the publish profile XML file that defines the cluster you want to connect to. Example: Cloud, Local.5Node. Default is Local.5Node.

.PARAMETER UseExistingClusterConnection
Indicates that the script should make use of an existing cluster connection that has already been established in the PowerShell session.  The cluster connection parameters configured in the publish profile are ignored.

.EXAMPLE
. deploy.ps1

Deploy a Debug build of the IoT project to a local 5-node cluster.

.EXAMPLE
. deploy.ps1 -Configuration Release -PublishProfileName Cloud

Deploy a Release build of the IoT project to a cluster defined in a publish profile file called Cloud.xml

#>

Param
(
    [String]
    $PublishProfileName = "Local.5Node",

    [Switch]
    $UseExistingClusterConnection
)

# These are the application types defined in the IoT solution.
# All application instances of these types will be deleted, and the types will be unregistered.
$applicationTypes = "IotAdminApplicationType", "IotIngestionApplicationType", "IotTenantApplicationType"

# Get references to the solution directory and the directory of this script.
$LocalDir = (Split-Path $MyInvocation.MyCommand.Path)
$SolutionDir = [System.IO.Path]::Combine((get-item $LocalDir).Parent.FullName, "src") 

# This is included with the solution
Import-Module "$LocalDir\functions.psm1"

# Get a publish profile from the profile XML files in the Deploy directory
if (!$PublishProfileName.EndsWith(".xml"))
{
    $PublishProfileName = $PublishProfileName + ".xml"
}

$PublishProfileFile = [System.IO.Path]::Combine($SolutionDir, "Deploy\$PublishProfileName")
$PublishProfile = Read-PublishProfile $PublishProfileFile

# Using the publish profile, connect to the SF cluster
if (-not $UseExistingClusterConnection)
{
    $ClusterConnectionParameters = $publishProfile.ClusterConnectionParameters
    if ($SecurityToken)
    {
        $ClusterConnectionParameters["SecurityToken"] = $SecurityToken
    }

    try
    {
        [void](Connect-ServiceFabricCluster @ClusterConnectionParameters)
    }
    catch [System.Fabric.FabricObjectClosedException]
    {
        Write-Warning "Service Fabric cluster may not be connected."
        throw
    }
}

Get-ServiceFabricApplication | Where-Object { $applicationTypes -contains $_.ApplicationTypeName } | Remove-ServiceFabricApplication -Force
Get-ServiceFabricApplicationType | Where-Object { $applicationTypes -contains $_.ApplicationTypeName } | Unregister-ServiceFabricApplicationType -Force