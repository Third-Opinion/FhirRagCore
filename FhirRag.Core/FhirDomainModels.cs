using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using Hl7.Fhir.Model;

namespace FhirRag.Core.Models;

/// <summary>
/// Core FHIR Patient domain model with validation and clinical relationship mappings
/// </summary>
public class FhirPatient
{
    [Required]
    public string Id { get; set; } = string.Empty;

    [Required]
    public string Identifier { get; set; } = string.Empty;

    [Required]
    [StringLength(100)]
    public string GivenName { get; set; } = string.Empty;

    [Required]
    [StringLength(100)]
    public string FamilyName { get; set; } = string.Empty;

    public DateTime? BirthDate { get; set; }

    [Required]
    public string Gender { get; set; } = string.Empty;

    public string? Phone { get; set; }

    public string? Email { get; set; }

    public Address? Address { get; set; }

    public List<string> ConditionIds { get; set; } = new();

    public List<string> ObservationIds { get; set; } = new();

    public List<string> MedicationIds { get; set; } = new();

    public List<string> ProcedureIds { get; set; } = new();

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    [JsonPropertyName("tenant_id")]
    public string TenantId { get; set; } = string.Empty;
}

/// <summary>
/// Core FHIR Observation domain model for clinical measurements and findings
/// </summary>
public class FhirObservation
{
    [Required]
    public string Id { get; set; } = string.Empty;

    [Required]
    public string PatientId { get; set; } = string.Empty;

    [Required]
    public ObservationStatus Status { get; set; } = ObservationStatus.Final;

    [Required]
    public CodeableConcept Code { get; set; } = new();

    public string? ValueString { get; set; }

    public decimal? ValueDecimal { get; set; }

    public string? ValueUnit { get; set; }

    public DateTime? EffectiveDateTime { get; set; }

    public DateTime? IssuedDateTime { get; set; }

    public List<string> CategoryCodes { get; set; } = new();

    public string? ClinicalInterpretation { get; set; }

    public string? Notes { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    [JsonPropertyName("tenant_id")]
    public string TenantId { get; set; } = string.Empty;
}

/// <summary>
/// Core FHIR Condition domain model for clinical conditions and diagnoses
/// </summary>
public class FhirCondition
{
    [Required]
    public string Id { get; set; } = string.Empty;

    [Required]
    public string PatientId { get; set; } = string.Empty;

    [Required]
    public CodeableConcept Code { get; set; } = new();

    public string? ClinicalStatus { get; set; }

    public string? VerificationStatus { get; set; }

    public string? Severity { get; set; }

    public DateTime? OnsetDateTime { get; set; }

    public DateTime? AbatementDateTime { get; set; }

    public DateTime? RecordedDate { get; set; }

    public List<string> CategoryCodes { get; set; } = new();

    public List<string> BodySiteCodes { get; set; } = new();

    public string? Notes { get; set; }

    public List<string> RelatedObservationIds { get; set; } = new();

    public List<string> RelatedMedicationIds { get; set; } = new();

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    [JsonPropertyName("tenant_id")]
    public string TenantId { get; set; } = string.Empty;
}

/// <summary>
/// Core FHIR Medication domain model for clinical medications and prescriptions
/// </summary>
public class FhirMedication
{
    [Required]
    public string Id { get; set; } = string.Empty;

    [Required]
    public string PatientId { get; set; } = string.Empty;

    [Required]
    public CodeableConcept Code { get; set; } = new();

    public string? Status { get; set; }

    public string? Intent { get; set; }

    public string? DosageInstruction { get; set; }

    public string? Route { get; set; }

    public string? Frequency { get; set; }

    public decimal? DoseQuantity { get; set; }

    public string? DoseUnit { get; set; }

    public DateTime? AuthoredOn { get; set; }

    public DateTime? EffectivePeriodStart { get; set; }

    public DateTime? EffectivePeriodEnd { get; set; }

    public List<string> ReasonCodes { get; set; } = new();

    public List<string> RelatedConditionIds { get; set; } = new();

    public string? Notes { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    [JsonPropertyName("tenant_id")]
    public string TenantId { get; set; } = string.Empty;
}

/// <summary>
/// Core FHIR Procedure domain model for clinical procedures and interventions
/// </summary>
public class FhirProcedure
{
    [Required]
    public string Id { get; set; } = string.Empty;

    [Required]
    public string PatientId { get; set; } = string.Empty;

    [Required]
    public CodeableConcept Code { get; set; } = new();

    public string? Status { get; set; }

    public List<string> CategoryCodes { get; set; } = new();

    public DateTime? PerformedDateTime { get; set; }

    public List<string> BodySiteCodes { get; set; } = new();

    public List<string> ReasonCodes { get; set; } = new();

    public List<string> RelatedConditionIds { get; set; } = new();

    public string? Outcome { get; set; }

    public string? Notes { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    [JsonPropertyName("tenant_id")]
    public string TenantId { get; set; } = string.Empty;
}
