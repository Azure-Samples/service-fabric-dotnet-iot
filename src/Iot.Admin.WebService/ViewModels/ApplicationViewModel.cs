// ------------------------------------------------------------
//  Copyright (c) Microsoft Corporation.  All rights reserved.
//  Licensed under the MIT License (MIT). See License.txt in the repo root for license information.
// ------------------------------------------------------------

namespace Iot.Admin.WebService.ViewModels
{
    using System.Fabric.Description;

    public class ApplicationViewModel
    {
        public ApplicationViewModel(string name, string status, string typeVersion, ApplicationParameterList parameters)
        {
            this.ApplicationName = name;
            this.ApplicationStatus = status;
            this.ApplicationTypeVersion = typeVersion;
            this.ApplicationParameters = parameters;
        }

        public string ApplicationName { get; private set; }

        public string ApplicationStatus { get; private set; }

        public string ApplicationTypeVersion { get; private set; }

        public ApplicationParameterList ApplicationParameters { get; private set; }
    }
}