using Argent.Models.Workflows.Execution;
using System;
using System.Collections.Generic;
using System.Text;

namespace Argent.Contracts.Workflows.Execution
{
    public interface IWorkRouter
    {
        public void Dispatch(WorkItem workItem, Action OnComplete);
    }
}
