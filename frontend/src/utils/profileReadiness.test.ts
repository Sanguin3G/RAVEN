import { describe, expect, it } from "vitest";
import { getProfileReadiness, hasUsableAcceptedProfile } from "./profileReadiness";

describe("profile readiness", () => {
  it("keeps an identity-only leftover row out of improvement", () => {
    const profile = {
      id: "profile-1",
      legalName: null,
      primaryIndustry: null,
      aiProvider: null,
      aiModel: null,
      promptTemplateVersion: null,
    };

    expect(getProfileReadiness(profile)).toBe("identity-only");
    expect(hasUsableAcceptedProfile(profile)).toBe(false);
  });

  it("accepts a model-created profile with at least one supported target", () => {
    const profile = {
      id: "profile-2",
      primaryIndustry: "Technology",
      aiProvider: "gemini",
      aiModel: "profile-model",
      promptTemplateVersion: "company-profile-v1",
    };

    expect(getProfileReadiness(profile)).toBe("sparse");
    expect(hasUsableAcceptedProfile(profile)).toBe(true);
  });
});
