using Argent.Models.Workflows.Auditing;
using System;
using System.Collections.Generic;
using System.Text;

namespace Argent.Contracts.Workflows.Execution
{
    public interface IWorkflowJournalManager
    {
        public void RecordEntry(WorkflowJournalEntry entry);
    }
}
