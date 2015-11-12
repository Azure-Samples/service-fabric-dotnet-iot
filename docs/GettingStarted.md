# Getting Started#

## Setup Your Machine ##
Follow [Prepare your development environment](https://azure.microsoft.com/en-us/documentation/articles/service-fabric-get-started/)

## Get the Source Code ##
1. Open Visual Studio 2015
2. Go to View -> Team Explorer
3. On the Connect page of the Team Explorer window, click the Clone dropdown located under the Local Git Repositories section
4. Enter the URL of this repository. 
5. If desired, change the local repository path
6. Click the Clone button

> You can perform the above using Git command line. 

## Begin working with the IoT solution ##
1. On the Home page of the Team Explorer window, open the solution by double-clicking IoT.sln listed under the Solutions section.  If you do not see it listed under the Solutions section, click the Open... link and navigate the local repository folder to open src/IoT.sln.
2. After opening the solution, wait for the Output window Package Manager pane to show "Restore complete" and "Total time" messages.
3. Go to Build -> Build Solution.
4. Review [Architecture & Usage Details](./Architecture.md).
5. Follow [Configuring, Deploying and Debugging the Solution](./ConfigureDeploy.md) to configure, deploy and optionally debug the solution.


## Azure Services ##
This sample reference implementation uses the following Azure Services

### Azure Storage (Table) ###
Follow [Create Storage Account](https://azure.microsoft.com/en-us/documentation/articles/storage-dotnet-how-to-use-tables/) to setup your storage account. 

### Azure Power BI ###
This Sample uses Azure PowerBI dashboard for reporting. The following is needed:

1. User name and password for Azure Active Directory user with access on Microsoft Azure Power BI.
2. Client Application created on the same Azure Active Directory with allowed permission on Power BI 
3. PowerBI Dashboard that uses ServiceFabricIoTDS dataset for reporting (*PowerBI Actor* creates this dataset dynamically). 

for an overview on the general steps to do the above [click here](https://msdn.microsoft.com/en-us/library/mt186158.aspx)

### Event Hub ###
Follow [Getting Started with Event Hubs](https://azure.microsoft.com/en-us/documentation/articles/event-hubs-csharp-ephcs-getstarted/) to provision a new Event Hub.