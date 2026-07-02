module.exports = {
  branches: [
    "main",
    { name: "dev", channel: "beta", prerelease: "beta" }
  ],
  tagFormat: "v${version}",
  plugins: [
    [
      "@semantic-release/commit-analyzer",
      { preset: "conventionalcommits" }
    ],
    [
      "@semantic-release/release-notes-generator",
      { preset: "conventionalcommits" }
    ],
    [
      "@droidsolutions-oss/semantic-release-nuget",
      {
        projectPath: "src/Transiever.SieveRuler/Transiever.SieveRuler.csproj",
        usePackageVersion: true,
        nugetRegistries: [
          {
            name: "nuget",
            type: "nuget",
            url: "https://api.nuget.org/v3/index.json",
            tokenEnvVar: "NUGET_API_KEY"
          }
        ]
      }
    ],
    [
      "@semantic-release/exec",
      {
        prepareCmd: "bash .github/scripts/build-release-assets.sh ${nextRelease.version}"
      }
    ],
    [
      "@semantic-release/github",
      {
        assets: [
          {
            path: "artifacts/srtx-win-x64.zip",
            name: "srtx-${nextRelease.gitTag}-win-x64.zip",
            label: "srtx Windows x64"
          },
          {
            path: "artifacts/srtx-win-x86.zip",
            name: "srtx-${nextRelease.gitTag}-win-x86.zip",
            label: "srtx Windows x86"
          },
          {
            path: "artifacts/srtx-linux-x64.tar.gz",
            name: "srtx-${nextRelease.gitTag}-linux-x64.tar.gz",
            label: "srtx Linux x64"
          }
        ]
      }
    ]
  ]
};
