using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace FhirRag.Core.Models;

/// <summary>
/// Codeable concept for FHIR resources
/// </summary>
public class CodeableConcept
{
    public List<Coding> Coding { get; set; } = new();
    
    public string? Text { get; set; }
}

/// <summary>
/// Coding for medical terminology
/// </summary>
public class Coding
{
    public string? System { get; set; }
    
    public string? Version { get; set; }
    
    [Required]
    public string Code { get; set; } = string.Empty;
    
    public string? Display { get; set; }
    
    public bool? UserSelected { get; set; }
}

/// <summary>
/// Address information
/// </summary>
public class Address
{
    public string? Use { get; set; }
    
    public string? Type { get; set; }
    
    public List<string> Line { get; set; } = new();
    
    public string? City { get; set; }
    
    public string? District { get; set; }
    
    public string? State { get; set; }
    
    public string? PostalCode { get; set; }
    
    public string? Country { get; set; }
}

/// <summary>
/// Identifier for FHIR resources
/// </summary>
public class Identifier
{
    public string? Use { get; set; }
    
    public CodeableConcept? Type { get; set; }
    
    public string? System { get; set; }
    
    [Required]
    public string Value { get; set; } = string.Empty;
    
    public string? Assigner { get; set; }
}

/// <summary>
/// Human name structure
/// </summary>
public class HumanName
{
    public string? Use { get; set; }
    
    public string? Text { get; set; }
    
    public List<string> Family { get; set; } = new();
    
    public List<string> Given { get; set; } = new();
    
    public List<string> Prefix { get; set; } = new();
    
    public List<string> Suffix { get; set; } = new();
}

/// <summary>
/// Contact point for communication
/// </summary>
public class ContactPoint
{
    public string? System { get; set; }
    
    [Required]
    public string Value { get; set; } = string.Empty;
    
    public string? Use { get; set; }
    
    public int? Rank { get; set; }
}

/// <summary>
/// Period of time
/// </summary>
public class Period
{
    public DateTime? Start { get; set; }
    
    public DateTime? End { get; set; }
}

/// <summary>
/// Quantity with value and unit
/// </summary>
public class Quantity
{
    public decimal? Value { get; set; }
    
    public string? Comparator { get; set; }
    
    public string? Unit { get; set; }
    
    public string? System { get; set; }
    
    public string? Code { get; set; }
}

/// <summary>
/// Range with low and high values
/// </summary>
public class Range
{
    public Quantity? Low { get; set; }
    
    public Quantity? High { get; set; }
}

/// <summary>
/// Reference to another resource
/// </summary>
public class Reference
{
    public string? ReferenceValue { get; set; }
    
    public string? Type { get; set; }
    
    public Identifier? Identifier { get; set; }
    
    public string? Display { get; set; }
}

/// <summary>
/// Annotation with author and text
/// </summary>
public class Annotation
{
    public Reference? AuthorReference { get; set; }
    
    public string? AuthorString { get; set; }
    
    public DateTime? Time { get; set; }
    
    [Required]
    public string Text { get; set; } = string.Empty;
}

/// <summary>
/// Clinical impression severity levels
/// </summary>
public enum Severity
{
    Mild,
    Moderate,
    Severe,
    Critical
}

/// <summary>
/// Observation status values
/// </summary>
public enum ObservationStatus
{
    Registered,
    Preliminary,
    Final,
    Amended,
    Corrected,
    Cancelled,
    EnteredInError,
    Unknown
}


/// <summary>
/// Common FHIR resource types
/// </summary>
public static class FhirResourceTypes
{
    public const string Patient = "Patient";
    public const string Observation = "Observation";
    public const string Condition = "Condition";
    public const string Medication = "Medication";
    public const string MedicationRequest = "MedicationRequest";
    public const string Procedure = "Procedure";
    public const string DiagnosticReport = "DiagnosticReport";
    public const string Encounter = "Encounter";
    public const string Organization = "Organization";
    public const string Practitioner = "Practitioner";
}

/// <summary>
/// Common SNOMED CT codes
/// </summary>
public static class SnomedCodes
{
    public const string System = "http://snomed.info/sct";
    
    // Common condition codes
    public const string Diabetes = "73211009";
    public const string Hypertension = "38341003";
    public const string Asthma = "195967001";
    public const string Depression = "35489007";
    
    // Common observation codes
    public const string BloodPressure = "75367002";
    public const string HeartRate = "364075005";
    public const string BodyWeight = "27113001";
    public const string BodyHeight = "50373000";
}

/// <summary>
/// Common LOINC codes for observations
/// </summary>
public static class LoincCodes
{
    public const string System = "http://loinc.org";
    
    // Vital signs
    public const string BloodPressureSystolic = "8480-6";
    public const string BloodPressureDiastolic = "8462-4";
    public const string HeartRate = "8867-4";
    public const string BodyWeight = "29463-7";
    public const string BodyHeight = "8302-2";
    public const string BodyMassIndex = "39156-5";
    
    // Laboratory values
    public const string Glucose = "33747-0";
    public const string HemoglobinA1c = "4548-4";
    public const string Cholesterol = "2093-3";
}

/// <summary>
/// Common ICD-10 codes
/// </summary>
public static class Icd10Codes
{
    public const string System = "http://hl7.org/fhir/sid/icd-10-cm";
    
    // Common conditions
    public const string Type2Diabetes = "E11";
    public const string EssentialHypertension = "I10";
    public const string Asthma = "J45";
    public const string Depression = "F33";
}