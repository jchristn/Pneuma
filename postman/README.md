# PolyPrompt integration simulation

These xAI contract and fixture files are copied from [PolyPrompt 3.4.0](https://github.com/jchristn/PolyPrompt/tree/f6e7662e70cd3e54568e0b37a103aa44e961462f/postman) (MIT). The fixture exercises actual Pneuma model-factory clients over HTTP, including streamed reasoning, fragmented function calls, continuation state, usage, and embeddings. It does not verify live model quality, credentials, account capabilities, or network behavior. The published-owner mock search was skipped because the CLI session is signed out.

With Postman CLI 1.70+ and .NET 10, start the simulation in one terminal:

```sh
postman simulation run postman/simulations/xai.sim.yaml --port 4912 --output ndjson
```

In another terminal:

```sh
PNEUMA_TEST_XAI_SIMULATION=http://127.0.0.1:4912/xai dotnet run --framework net10.0 --project src/Test.Automated -- --suite PolyPromptSimulation,ExternalServices
```

The simulation suite is registered only when `PNEUMA_TEST_XAI_SIMULATION` is set. Only point this variable at a local fixture: the suite sends a dummy key. Check simulation request events to confirm requests reach `/v1/responses` and `/v1/embeddings`. Stop the simulation with Ctrl-C. No Postman login or publication is required.
