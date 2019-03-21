﻿using AccessibilityInsights.Extensions.Interfaces.IssueReporting;
using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace AccessibilityInsights.SharedUx.Controls.SettingsTabs
{
    [Export(typeof(IIssueReporting))]
    public class TestIssueProvider : IIssueReporting
    {
       public string ServiceName => "Ashwins test extension";
        //e3ecd010-c9e1-44b1-a6da-24fe4e3f117c
        public Guid StableIdentifier => Guid.Parse("27f21dff-2fb3-4833-be55-25787fce3e17");

        public bool IsConfigured => true;

        public bool CanAttachFiles => true;

        public ReporterFabricIcon Logo => ReporterFabricIcon.GitHubLogo;

        public string LogoText => null;

        public Task<IIssueResult> FileIssueAsync(IssueInformation issueInfo)
        {
            return new Task<IIssueResult>(() => { return null; });
        }

        public Task RestoreConfigurationAsync(string serializedConfig)
        {
            return Task.Run(() => {
                Thread.Sleep(5000);
                return Task.CompletedTask;
            });

        }

        public IssueConfigurationControl RetrieveConfigurationControl(Action UpdateSaveButton)
        {
            //Thread.Sleep(5000);
            return new TestIssueConfigurationControl(UpdateSaveButton);
        }
    }
}
