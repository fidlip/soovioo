/** Matrika's RAST číselník (countries) — see Student.countryCode. */
export interface Country {
  code: string;
  name: string;
}

/** Matrika's RAUJ číselník (municipalities) — see Student.municipalityCode.
 * Carries its own district code so OKRESB can be derived on export without a
 * separate district picker anywhere in the UI. */
export interface Municipality {
  code: string;
  name: string;
  districtCode: string;
}
