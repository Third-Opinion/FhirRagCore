using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using FhirRag.Core.Models;

namespace FhirRag.Core.Validation;

/// <summary>
/// Validation helpers for FHIR domain models
/// </summary>
public static class ValidationHelpers
{
    /// <summary>
    /// Validates a FHIR patient model
    /// </summary>
    public static FhirValidationResult ValidatePatient(FhirPatient patient)
    {
        var validationResults = new List<ValidationResult>();
        var context = new ValidationContext(patient);

        Validator.TryValidateObject(patient, context, validationResults, true);

        // Additional business rule validations
        if (patient.BirthDate.HasValue && patient.BirthDate.Value > DateTime.Now)
        {
            validationResults.Add(new ValidationResult("Birth date cannot be in the future"));
        }

        if (!IsValidGender(patient.Gender))
        {
            validationResults.Add(new ValidationResult("Invalid gender value"));
        }

        return new FhirValidationResult
        {
            IsValid = !validationResults.Any(),
            Errors = validationResults.Select(vr => vr.ErrorMessage ?? "Unknown error").ToList()
        };
    }

    /// <summary>
    /// Validates a FHIR observation model
    /// </summary>
    public static FhirValidationResult ValidateObservation(FhirObservation observation)
    {
        var validationResults = new List<ValidationResult>();
        var context = new ValidationContext(observation);

        Validator.TryValidateObject(observation, context, validationResults, true);

        // Additional business rule validations
        if (observation.EffectiveDateTime.HasValue && observation.EffectiveDateTime.Value > DateTime.Now)
        {
            validationResults.Add(new ValidationResult("Effective date cannot be in the future"));
        }

        if (observation.ValueDecimal.HasValue && observation.ValueDecimal.Value < 0)
        {
            validationResults.Add(new ValidationResult("Numeric values cannot be negative for most observations"));
        }

        return new FhirValidationResult
        {
            IsValid = !validationResults.Any(),
            Errors = validationResults.Select(vr => vr.ErrorMessage ?? "Unknown error").ToList()
        };
    }

    /// <summary>
    /// Validates a FHIR condition model
    /// </summary>
    public static FhirValidationResult ValidateCondition(FhirCondition condition)
    {
        var validationResults = new List<ValidationResult>();
        var context = new ValidationContext(condition);

        Validator.TryValidateObject(condition, context, validationResults, true);

        // Additional business rule validations
        if (condition.OnsetDateTime.HasValue && condition.OnsetDateTime.Value > DateTime.Now)
        {
            validationResults.Add(new ValidationResult("Onset date cannot be in the future"));
        }

        if (condition.OnsetDateTime.HasValue && condition.AbatementDateTime.HasValue &&
            condition.AbatementDateTime.Value < condition.OnsetDateTime.Value)
        {
            validationResults.Add(new ValidationResult("Abatement date cannot be before onset date"));
        }

        return new FhirValidationResult
        {
            IsValid = !validationResults.Any(),
            Errors = validationResults.Select(vr => vr.ErrorMessage ?? "Unknown error").ToList()
        };
    }

    /// <summary>
    /// Validates terminology coding
    /// </summary>
    public static bool IsValidCoding(Coding coding)
    {
        if (string.IsNullOrWhiteSpace(coding.Code))
            return false;

        // Validate common coding systems
        return coding.System switch
        {
            SnomedCodes.System => IsValidSnomedCode(coding.Code),
            LoincCodes.System => IsValidLoincCode(coding.Code),
            Icd10Codes.System => IsValidIcd10Code(coding.Code),
            _ => true // Allow other systems
        };
    }

    /// <summary>
    /// Validates SNOMED CT code format
    /// </summary>
    public static bool IsValidSnomedCode(string code)
    {
        return !string.IsNullOrWhiteSpace(code) &&
               code.All(char.IsDigit) &&
               code.Length >= 6 &&
               code.Length <= 18;
    }

    /// <summary>
    /// Validates LOINC code format
    /// </summary>
    public static bool IsValidLoincCode(string code)
    {
        if (string.IsNullOrWhiteSpace(code))
            return false;

        var parts = code.Split('-');
        return parts.Length == 2 &&
               parts[0].All(char.IsDigit) &&
               parts[1].All(char.IsDigit) &&
               parts[0].Length >= 4 &&
               parts[1].Length == 1;
    }

    /// <summary>
    /// Validates ICD-10 code format
    /// </summary>
    public static bool IsValidIcd10Code(string code)
    {
        if (string.IsNullOrWhiteSpace(code))
            return false;

        return code.Length >= 3 &&
               code.Length <= 7 &&
               char.IsLetter(code[0]) &&
               code.Skip(1).Take(2).All(char.IsDigit);
    }

    /// <summary>
    /// Validates gender value
    /// </summary>
    public static bool IsValidGender(string gender)
    {
        var validGenders = new[] { "male", "female", "other", "unknown" };
        return validGenders.Contains(gender?.ToLowerInvariant());
    }

    /// <summary>
    /// Validates tenant ID format
    /// </summary>
    public static bool IsValidTenantId(string tenantId)
    {
        return !string.IsNullOrWhiteSpace(tenantId) &&
               tenantId.Length >= 3 &&
               tenantId.Length <= 50 &&
               tenantId.All(c => char.IsLetterOrDigit(c) || c == '-' || c == '_');
    }

    /// <summary>
    /// Validates JSON serialization/deserialization
    /// </summary>
    public static bool IsValidJson<T>(T obj) where T : class
    {
        try
        {
            var json = JsonSerializer.Serialize(obj);
            var deserialized = JsonSerializer.Deserialize<T>(json);
            return deserialized != null;
        }
        catch
        {
            return false;
        }
    }
}

/// <summary>
/// Validation result container
/// </summary>
public class FhirValidationResult
{
    public bool IsValid { get; set; }
    public List<string> Errors { get; set; } = new();
    public List<string> Warnings { get; set; } = new();
}

/// <summary>
/// Custom validation attributes for FHIR data
/// </summary>
public class ValidFhirCodeAttribute : ValidationAttribute
{
    protected override System.ComponentModel.DataAnnotations.ValidationResult? IsValid(object? value, ValidationContext validationContext)
    {
        if (value is Coding coding)
        {
            return ValidationHelpers.IsValidCoding(coding)
                ? System.ComponentModel.DataAnnotations.ValidationResult.Success
                : new System.ComponentModel.DataAnnotations.ValidationResult("Invalid FHIR coding format");
        }

        return new System.ComponentModel.DataAnnotations.ValidationResult("Value must be a valid Coding object");
    }
}

/// <summary>
/// Validation attribute for tenant ID
/// </summary>
public class ValidTenantIdAttribute : ValidationAttribute
{
    protected override System.ComponentModel.DataAnnotations.ValidationResult? IsValid(object? value, ValidationContext validationContext)
    {
        if (value is string tenantId)
        {
            return ValidationHelpers.IsValidTenantId(tenantId)
                ? System.ComponentModel.DataAnnotations.ValidationResult.Success
                : new System.ComponentModel.DataAnnotations.ValidationResult("Invalid tenant ID format");
        }

        return new System.ComponentModel.DataAnnotations.ValidationResult("Tenant ID must be a valid string");
    }
}