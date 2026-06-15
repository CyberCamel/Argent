namespace Argent.Models.Authorization;

public static class ResourceActions
{
    public static class Workflow
    {
        public const string View = "view";
        public const string Model = "model";
        public const string Run = "run";
        public const string Delete = "delete";
        public static readonly string[] Owner = [View, Model, Run, Delete];
    }

    public static class Form
    {
        public const string View = "view";
        public const string Design = "design";
        public const string Delete = "delete";
        public static readonly string[] Owner = [View, Design, Delete];
    }

    public static class DomainObject
    {
        public const string View = "view";
        public const string Create = "create";
        public const string Edit = "edit";
        public const string Delete = "delete";
    }

    public static class WorkflowInstance
    {
        public const string View = "view";
        public const string Cancel = "cancel";
    }

    public static class UserTask
    {
        public const string View = "view";
        public const string Complete = "complete";
    }
}
