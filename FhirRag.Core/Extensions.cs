using System.Text.Json;
using FhirRag.Core.Models;
using Hl7.Fhir.Model;
using FhirPatient = FhirRag.Core.Models.FhirPatient;
using FhirObservation = FhirRag.Core.Models.FhirObservation;
using FhirCondition = FhirRag.Core.Models.FhirCondition;
using FhirMedication = FhirRag.Core.Models.FhirMedication;
using FhirProcedure = FhirRag.Core.Models.FhirProcedure;
using LocalCodeableConcept = FhirRag.Core.Models.CodeableConcept;
using LocalCoding = FhirRag.Core.Models.Coding;

namespace FhirRag.Core.Extensions;

/// <summary>
/// Extension methods for FHIR domain models
/// </summary>
public static class FhirModelExtensions
{
    /// <summary>
    /// Converts FhirPatient to FHIR R4 Patient resource
    /// </summary>
    public static Patient ToFhirResource(this FhirPatient patient)
    {
        var fhirPatient = new Patient
        {
            Id = patient.Id,
            Active = true,
            BirthDate = patient.BirthDate?.ToString("yyyy-MM-dd"),
            Gender = ParseGender(patient.Gender)
        };

        // Add identifier
        fhirPatient.Identifier.Add(new Hl7.Fhir.Model.Identifier
        {
            Value = patient.Identifier,
            Use = Hl7.Fhir.Model.Identifier.IdentifierUse.Usual
        });

        // Add name
        fhirPatient.Name.Add(new Hl7.Fhir.Model.HumanName
        {
            Use = Hl7.Fhir.Model.HumanName.NameUse.Official,
            Given = new[] { patient.GivenName },
            Family = patient.FamilyName
        });

        // Add contact information
        if (!string.IsNullOrEmpty(patient.Phone))
        {
            fhirPatient.Telecom.Add(new Hl7.Fhir.Model.ContactPoint
            {
                System = Hl7.Fhir.Model.ContactPoint.ContactPointSystem.Phone,
                Value = patient.Phone,
                Use = Hl7.Fhir.Model.ContactPoint.ContactPointUse.Home
            });
        }

        if (!string.IsNullOrEmpty(patient.Email))
        {
            fhirPatient.Telecom.Add(new Hl7.Fhir.Model.ContactPoint
            {
                System = Hl7.Fhir.Model.ContactPoint.ContactPointSystem.Email,
                Value = patient.Email,
                Use = Hl7.Fhir.Model.ContactPoint.ContactPointUse.Home
            });
        }

        return fhirPatient;
    }

    /// <summary>
    /// Converts FhirObservation to FHIR R4 Observation resource
    /// </summary>
    public static Observation ToFhirResource(this FhirObservation observation)
    {
        var fhirObservation = new Observation
        {
            Id = observation.Id,
            Status = (Hl7.Fhir.Model.ObservationStatus)observation.Status,
            Subject = new ResourceReference($"Patient/{observation.PatientId}"),
            Code = observation.Code.ToFhirCodeableConcept()
        };

        // Add effective date
        if (observation.EffectiveDateTime.HasValue)
        {
            fhirObservation.Effective = new FhirDateTime(observation.EffectiveDateTime.Value);
        }

        // Add value
        if (!string.IsNullOrEmpty(observation.ValueString))
        {
            fhirObservation.Value = new FhirString(observation.ValueString);
        }
        else if (observation.ValueDecimal.HasValue)
        {
            var quantity = new Hl7.Fhir.Model.Quantity
            {
                Value = observation.ValueDecimal.Value
            };

            if (!string.IsNullOrEmpty(observation.ValueUnit))
            {
                quantity.Unit = observation.ValueUnit;
            }

            fhirObservation.Value = quantity;
        }

        return fhirObservation;
    }

    /// <summary>
    /// Creates a clinical relationship mapping between resources
    /// </summary>
    public static void AddClinicalRelationship(this FhirPatient patient, string resourceType, string resourceId)
    {
        switch (resourceType.ToLowerInvariant())
        {
            case "condition":
                if (!patient.ConditionIds.Contains(resourceId))
                    patient.ConditionIds.Add(resourceId);
                break;
            case "observation":
                if (!patient.ObservationIds.Contains(resourceId))
                    patient.ObservationIds.Add(resourceId);
                break;
            case "medication":
                if (!patient.MedicationIds.Contains(resourceId))
                    patient.MedicationIds.Add(resourceId);
                break;
            case "procedure":
                if (!patient.ProcedureIds.Contains(resourceId))
                    patient.ProcedureIds.Add(resourceId);
                break;
        }

        patient.UpdatedAt = DateTime.UtcNow;
    }

    /// <summary>
    /// Gets all related resource IDs for a patient
    /// </summary>
    public static List<string> GetAllRelatedResourceIds(this FhirPatient patient)
    {
        var allIds = new List<string>();
        allIds.AddRange(patient.ConditionIds);
        allIds.AddRange(patient.ObservationIds);
        allIds.AddRange(patient.MedicationIds);
        allIds.AddRange(patient.ProcedureIds);
        return allIds.Distinct().ToList();
    }

    /// <summary>
    /// Converts our CodeableConcept to FHIR CodeableConcept
    /// </summary>
    public static Hl7.Fhir.Model.CodeableConcept ToFhirCodeableConcept(this LocalCodeableConcept concept)
    {
        var fhirConcept = new Hl7.Fhir.Model.CodeableConcept();

        foreach (var coding in concept.Coding)
        {
            fhirConcept.Coding.Add(new Hl7.Fhir.Model.Coding
            {
                System = coding.System,
                Code = coding.Code,
                Display = coding.Display,
                Version = coding.Version
            });
        }

        if (!string.IsNullOrEmpty(concept.Text))
        {
            fhirConcept.Text = concept.Text;
        }

        return fhirConcept;
    }

    /// <summary>
    /// Creates a deep clone of a FHIR model
    /// </summary>
    public static T DeepClone<T>(this T model) where T : class
    {
        var json = JsonSerializer.Serialize(model);
        return JsonSerializer.Deserialize<T>(json) ?? throw new InvalidOperationException("Failed to clone object");
    }

    /// <summary>
    /// Checks if a resource has been enriched
    /// </summary>
    public static bool IsEnriched(this EnrichedFhirResource resource)
    {
        return resource.NlpResult != null ||
               resource.TerminologyMappings.Any() ||
               resource.RiskScores.Any() ||
               resource.Embeddings?.Any() == true;
    }

    /// <summary>
    /// Gets the primary coding from a CodeableConcept
    /// </summary>
    public static LocalCoding? GetPrimaryCoding(this LocalCodeableConcept concept)
    {
        return concept.Coding.FirstOrDefault(c => c.UserSelected == true) ??
               concept.Coding.FirstOrDefault();
    }

    /// <summary>
    /// Adds enrichment metadata to a processing result
    /// </summary>
    public static void AddEnrichmentStep(this ProcessingResult result, string stepName, string description, Dictionary<string, object>? data = null)
    {
        var step = new ProcessingStep
        {
            Name = stepName,
            Description = description,
            Status = ProcessingStepStatus.InProgress,
            Data = data ?? new Dictionary<string, object>()
        };

        result.Steps.Add(step);
    }

    /// <summary>
    /// Completes a processing step
    /// </summary>
    public static void CompleteStep(this ProcessingResult result, string stepName, Dictionary<string, object>? data = null)
    {
        var step = result.Steps.FirstOrDefault(s => s.Name == stepName);
        if (step != null)
        {
            step.Status = ProcessingStepStatus.Completed;
            step.CompletedAt = DateTime.UtcNow;

            if (data != null)
            {
                foreach (var kvp in data)
                {
                    step.Data[kvp.Key] = kvp.Value;
                }
            }
        }
    }

    /// <summary>
    /// Marks a processing step as failed
    /// </summary>
    public static void FailStep(this ProcessingResult result, string stepName, string errorMessage)
    {
        var step = result.Steps.FirstOrDefault(s => s.Name == stepName);
        if (step != null)
        {
            step.Status = ProcessingStepStatus.Failed;
            step.ErrorMessage = errorMessage;
            step.CompletedAt = DateTime.UtcNow;
        }

        result.Status = ProcessingStatus.Failed;
        result.ErrorMessage = errorMessage;
    }

    /// <summary>
    /// Parses gender string to FHIR AdministrativeGender
    /// </summary>
    private static Hl7.Fhir.Model.AdministrativeGender? ParseGender(string gender)
    {
        return gender?.ToLowerInvariant() switch
        {
            "male" => Hl7.Fhir.Model.AdministrativeGender.Male,
            "female" => Hl7.Fhir.Model.AdministrativeGender.Female,
            "other" => Hl7.Fhir.Model.AdministrativeGender.Other,
            "unknown" => Hl7.Fhir.Model.AdministrativeGender.Unknown,
            _ => null
        };
    }
}

/// <summary>
/// Extension methods for clinical relationship management
/// </summary>
public static class ClinicalRelationshipExtensions
{
    /// <summary>
    /// Links a condition to related observations
    /// </summary>
    public static void LinkToObservations(this FhirCondition condition, params string[] observationIds)
    {
        foreach (var observationId in observationIds)
        {
            if (!condition.RelatedObservationIds.Contains(observationId))
            {
                condition.RelatedObservationIds.Add(observationId);
            }
        }
        condition.UpdatedAt = DateTime.UtcNow;
    }

    /// <summary>
    /// Links a condition to related medications
    /// </summary>
    public static void LinkToMedications(this FhirCondition condition, params string[] medicationIds)
    {
        foreach (var medicationId in medicationIds)
        {
            if (!condition.RelatedMedicationIds.Contains(medicationId))
            {
                condition.RelatedMedicationIds.Add(medicationId);
            }
        }
        condition.UpdatedAt = DateTime.UtcNow;
    }

    /// <summary>
    /// Links a medication to related conditions
    /// </summary>
    public static void LinkToConditions(this FhirMedication medication, params string[] conditionIds)
    {
        foreach (var conditionId in conditionIds)
        {
            if (!medication.RelatedConditionIds.Contains(conditionId))
            {
                medication.RelatedConditionIds.Add(conditionId);
            }
        }
        medication.UpdatedAt = DateTime.UtcNow;
    }

    /// <summary>
    /// Links a procedure to related conditions
    /// </summary>
    public static void LinkToConditions(this FhirProcedure procedure, params string[] conditionIds)
    {
        foreach (var conditionId in conditionIds)
        {
            if (!procedure.RelatedConditionIds.Contains(conditionId))
            {
                procedure.RelatedConditionIds.Add(conditionId);
            }
        }
        procedure.UpdatedAt = DateTime.UtcNow;
    }
}