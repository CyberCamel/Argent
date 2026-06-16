using Argent.Contracts.DomainObjects;
using Argent.Models.DomainObjects;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Argent.Web.Pages.DomainObjects.Records;

public class FormModel(IDomainObjectDefinitionService _definitions, IDomainObjectStore _store) : PageModel
{
    [FromRoute] public string Key { get; set; } = "";

    [BindProperty(SupportsGet = true)]
    public Guid? Id { get; set; }

    public DomainObjectDefinition Definition { get; set; } = default!;

    [BindProperty]
    public Dictionary<string, string?> FormValues { get; set; } = new();

    public Dictionary<string, List<DomainOption>> ReferenceOptions { get; set; } = new();

    public bool IsEdit => Id.HasValue;

    public async Task<IActionResult> OnGet()
    {
        var def = await _definitions.GetPublishedDefinitionAsync(Key);
        if (def == null) return NotFound();
        Definition = def;

        await LoadReferenceOptionsAsync();

        if (Id.HasValue)
        {
            var record = await _store.GetAsync(Key, Id.Value);
            if (record == null) return NotFound();
            foreach (var kvp in record.Values)
                FormValues[kvp.Key] = FormatValue(kvp.Value);
        }

        return Page();
    }

    public async Task<IActionResult> OnPost()
    {
        var def = await _definitions.GetPublishedDefinitionAsync(Key);
        if (def == null) return NotFound();
        Definition = def;

        if (!ModelState.IsValid)
        {
            await LoadReferenceOptionsAsync();
            return Page();
        }

        var values = CoerceValues(def);
        var user = User.Identity?.Name;

        if (Id.HasValue)
            await _store.UpdateAsync(Key, Id.Value, values, user);
        else
            await _store.CreateAsync(Key, values, user);

        return RedirectToPage("Index", new { Key });
    }

    private Dictionary<string, object?> CoerceValues(DomainObjectDefinition def)
    {
        var result = new Dictionary<string, object?>();
        foreach (var prop in def.Properties)
        {
            FormValues.TryGetValue(prop.Key, out var raw);
            result[prop.Key] = prop.Type switch
            {
                DomainPropertyType.Number => double.TryParse(raw, out var d) ? d : null,
                DomainPropertyType.Boolean => raw is "true" or "on",
                _ => string.IsNullOrEmpty(raw) ? null : raw
            };
        }
        return result;
    }

    private async Task LoadReferenceOptionsAsync()
    {
        foreach (var prop in Definition.Properties.Where(p => p.Type == DomainPropertyType.Reference && !string.IsNullOrEmpty(p.ReferenceTargetKey)))
        {
            var refDef = await _definitions.GetPublishedDefinitionAsync(prop.ReferenceTargetKey!);
            var labelField = prop.ReferenceDisplayProperty ?? refDef?.TitleProperty ?? refDef?.Properties.FirstOrDefault()?.Key ?? "id";
            var options = await _store.GetOptionsAsync(prop.ReferenceTargetKey!, "id", labelField);
            ReferenceOptions[prop.Key] = options;
        }
    }

    private static string? FormatValue(object? val) => val switch
    {
        null => null,
        bool b => b ? "true" : "false",
        _ => val.ToString()
    };
}
