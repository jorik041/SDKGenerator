#if !DISABLE_PLAYFABCLIENT_API && ENABLE_PLAYFABENTITY_API
using PlayFab.EntityModels;
using PlayFab.Internal;
using System;
using System.Collections.Generic;
using PlayFab.Json;
using UnityEngine;

namespace PlayFab.UUnit
{
    /// <summary>
    /// A real system would potentially run only the client or server API, and not both.
    /// But, they still interact with eachother directly.
    /// The tests can't be independent for Client/Server, as the sequence of calls isn't really independent for real-world scenarios.
    /// The client logs in, which triggers a server, and then back and forth.
    /// For the purpose of testing, they each have pieces of information they share with one another, and that sharing makes various calls possible.
    /// </summary>
    public class EntityApiTests : UUnitTestCase
    {
        private TestTitleDataLoader.TestTitleData testTitleData;

        // Test-data constants
        private const string TEST_OBJ_NAME = "testCounter";
        // Test variables
        private string _entityId;
        private string _testFileUrl;
        private string _testFileChecksum;
        private int _testInteger;

        public override void SetUp(UUnitTestContext testContext)
        {
            testTitleData = TestTitleDataLoader.LoadTestTitleData();

            // Verify all the inputs won't cause crashes in the tests
            var titleInfoSet = !string.IsNullOrEmpty(PlayFabSettings.TitleId);
            if (!titleInfoSet)
                testContext.Skip(); // We cannot do client tests if the titleId is not given

            if (testTitleData.extraHeaders != null)
                foreach (var pair in testTitleData.extraHeaders)
                    PlayFabHttp.GlobalHeaderInjection[pair.Key] = pair.Value;
        }

        public override void Tick(UUnitTestContext testContext)
        {
            // Do nothing, because the test finishes asynchronously
        }

        public override void ClassTearDown()
        {
            PlayFabEntityAPI.ForgetAllCredentials();
        }

        private void SharedErrorCallback(PlayFabError error)
        {
            // This error was not expected.  Report it and fail.
            ((UUnitTestContext)error.CustomData).Fail(error.GenerateErrorReport());
        }

        /// <summary>
        /// CLIENT/ENTITY API
        /// Log in or create a user, track their PlayFabId
        /// </summary>
        [UUnitTest]
        public void EntityClientLogin(UUnitTestContext testContext)
        {
            var loginRequest = new ClientModels.LoginWithCustomIDRequest
            {
                CustomId = PlayFabSettings.BuildIdentifier,
                CreateAccount = true,
            };
            PlayFabClientAPI.LoginWithCustomID(loginRequest, PlayFabUUnitUtils.ApiActionWrapper<ClientModels.LoginResult>(testContext, LoginCallback), PlayFabUUnitUtils.ApiActionWrapper<PlayFabError>(testContext, SharedErrorCallback), testContext);
        }
        private void LoginCallback(ClientModels.LoginResult result)
        {
            var testContext = (UUnitTestContext)result.CustomData;
            testContext.True(PlayFabClientAPI.IsClientLoggedIn(), "User login failed");
            testContext.EndTest(UUnitFinishState.PASSED, PlayFabSettings.TitleId + ", " + result.PlayFabId);
        }

        /// <summary>
        /// ENTITY API
        /// Verify that a client login can be converted into an entity token
        /// </summary>
        [UUnitTest]
        public void GetEntityToken(UUnitTestContext testContext)
        {
            var tokenRequest = new GetEntityTokenRequest();
            PlayFabEntityAPI.GetEntityToken(tokenRequest, PlayFabUUnitUtils.ApiActionWrapper<GetEntityTokenResponse>(testContext, GetTokenCallback), PlayFabUUnitUtils.ApiActionWrapper<PlayFabError>(testContext, SharedErrorCallback), testContext);
        }
        private void GetTokenCallback(GetEntityTokenResponse result)
        {
            var testContext = (UUnitTestContext)result.CustomData;

            _entityId = result.EntityId;
            testContext.StringEquals(EntityTypes.title_player_account.ToString(), result.EntityType, "GetEntityToken EntityType not expected: " + result.EntityType);

            testContext.True(PlayFabClientAPI.IsClientLoggedIn(), "Get Entity Token failed");
            testContext.EndTest(UUnitFinishState.PASSED, PlayFabSettings.TitleId + ", " + result.EntityToken);
        }

        /// <summary>
        /// ENTITY API
        /// Test a sequence of calls that modifies entity objects,
        ///   and verifies that the next sequential API call contains updated information.
        /// Verify that the object is correctly modified on the next call.
        /// </summary>
        [UUnitTest]
        public void ObjectApi(UUnitTestContext testContext)
        {
            var getRequest = new GetObjectsRequest { EntityId = _entityId, EntityType = EntityTypes.title_player_account, EscapeObject = true };
            PlayFabEntityAPI.GetObjects(getRequest, PlayFabUUnitUtils.ApiActionWrapper<GetObjectsResponse>(testContext, GetObjectCallback1), PlayFabUUnitUtils.ApiActionWrapper<PlayFabError>(testContext, SharedErrorCallback), testContext);
        }
        private void GetObjectCallback1(GetObjectsResponse result)
        {
            var testContext = (UUnitTestContext)result.CustomData;

            _testInteger = 0; // Default if the data isn't present
            foreach (var eachObj in result.Objects)
                if (eachObj.ObjectName == TEST_OBJ_NAME)
                    int.TryParse(eachObj.EscapedDataObject, out _testInteger);

            _testInteger = (_testInteger + 1) % 100; // This test is about the Expected value changing - but not testing more complicated issues like bounds

            var updateRequest = new SetObjectsRequest
            {
                EntityId = _entityId,
                EntityType = EntityTypes.title_player_account,
                Objects = new List<SetObject> {
                    new SetObject{ ObjectName = TEST_OBJ_NAME, Unstructured = true, DataObject = _testInteger }
                }
            };
            PlayFabEntityAPI.SetObjects(updateRequest, PlayFabUUnitUtils.ApiActionWrapper<SetObjectsResponse>(testContext, UpdateObjectCallback), PlayFabUUnitUtils.ApiActionWrapper<PlayFabError>(testContext, SharedErrorCallback), testContext);
        }
        private void UpdateObjectCallback(SetObjectsResponse result)
        {
            var testContext = (UUnitTestContext)result.CustomData;

            var getRequest = new GetObjectsRequest { EntityId = _entityId, EntityType = EntityTypes.title_player_account, EscapeObject = true };
            PlayFabEntityAPI.GetObjects(getRequest, PlayFabUUnitUtils.ApiActionWrapper<GetObjectsResponse>(testContext, GetObjectCallback2), PlayFabUUnitUtils.ApiActionWrapper<PlayFabError>(testContext, SharedErrorCallback), testContext);
        }
        private void GetObjectCallback2(GetObjectsResponse result)
        {
            var testContext = (UUnitTestContext)result.CustomData;

            var actualInteger = -100; // Default if the data isn't present
            testContext.IntEquals(result.Objects.Count, 1, "Incorrect number of entity objects: " + result.Objects.Count);
            testContext.StringEquals(result.Objects[0].ObjectName, TEST_OBJ_NAME, "Expected Test object not found: " + result.Objects[0].ObjectName);
            actualInteger = int.Parse(result.Objects[0].EscapedDataObject);
            testContext.True(actualInteger != -100, "Entity object not set");
            testContext.IntEquals(_testInteger, actualInteger, "Entity Object was not updated: " + actualInteger + "!=" + _testInteger);

            testContext.EndTest(UUnitFinishState.PASSED, null);
        }

        /// <summary>
        /// CLIENT API
        /// Test a sequence of calls that modifies saved data,
        ///   and verifies that the next sequential API call contains updated data.
        /// Verify that the data is correctly modified on the next call.
        /// Parameter types tested: string, Dictionary&lt;string, string>, DateTime
        /// </summary>
        [UUnitTest]
        public void FileApi(UUnitTestContext testContext)
        {
            var getRequest = new GetFilesRequest { EntityId = _entityId, EntityType = EntityTypes.title_player_account };
            PlayFabEntityAPI.GetFiles(getRequest, PlayFabUUnitUtils.ApiActionWrapper<GetFilesResponse>(testContext, GetFilesCallback1), PlayFabUUnitUtils.ApiActionWrapper<PlayFabError>(testContext, SharedErrorCallback), testContext);
        }
        private void GetFilesCallback1(GetFilesResponse result)
        {
            var testContext = (UUnitTestContext)result.CustomData;

            _testFileUrl = _testFileChecksum = null;
            foreach (var eachFileDesc in result.Metadata)
            {
                if (eachFileDesc.FileName == TEST_OBJ_NAME)
                {
                    _testFileUrl = eachFileDesc.DownloadUrl;
                    _testFileChecksum = eachFileDesc.Checksum;
                }
            }

            if (string.IsNullOrEmpty(_testFileUrl))
                OnSimpleGet1(testContext, BitConverter.GetBytes(0)); // Default if the data isn't present
            else
                PlayFabHttp.SimpleGetCall(
                    _testFileUrl,
                    (getResult) => { OnSimpleGet1(testContext, getResult); },
                    (errorStr) => { testContext.Fail(errorStr); }
                );
        }
        private void OnSimpleGet1(UUnitTestContext testContext, byte[] payload)
        {
            _testInteger = 0;
            try { _testInteger = BitConverter.ToInt32(payload, 0); } catch (Exception) { } // Lots of potential problems we don't care about, because any failure means use default above
            _testInteger = (_testInteger + 1) % 100; // This test is about the Expected value changing - but not testing more complicated issues like bounds

            InitiateFileUploads(testContext);
        }
        private void InitiateFileUploads(UUnitTestContext testContext)
        {
            var updateRequest = new InitiateFileUploadsRequest
            {
                EntityId = _entityId,
                EntityType = EntityTypes.title_player_account,
                FileNames = new List<string> { TEST_OBJ_NAME }
            };
            PlayFabEntityAPI.InitiateFileUploads(updateRequest, PlayFabUUnitUtils.ApiActionWrapper<InitiateFileUploadsResponse>(testContext, InitiateFileUploadsCallback), PlayFabUUnitUtils.ApiActionWrapper<PlayFabError>(testContext, InitiateFileUploadsFailed), testContext);
        }
        private void InitiateFileUploadsFailed(PlayFabError error)
        {
            Debug.LogWarning("InitiateFileUploadsFailed " + error.Error);
            var testContext = (UUnitTestContext)error.CustomData;
            if (error.Error == PlayFabErrorCode.EntityFileOperationPending)
            {
                var request = new AbortFileUploadsRequest
                {
                    EntityId = _entityId,
                    EntityType = EntityTypes.title_player_account,
                    FileNames = new List<string> { TEST_OBJ_NAME }
                };
                Debug.Log("For Siva: " + _entityId + " " + EntityTypes.title_player_account + ", AbortFileUploadsRequest:\n" + JsonWrapper.SerializeObject(request));
                PlayFabEntityAPI.AbortFileUploads(request, (result) => { InitiateFileUploads(testContext); }, PlayFabUUnitUtils.ApiActionWrapper<PlayFabError>(testContext, SharedErrorCallback), testContext);
            }
            else
            {
                SharedErrorCallback(error);
            }
        }
        private void InitiateFileUploadsCallback(InitiateFileUploadsResponse result)
        {
            var testContext = (UUnitTestContext)result.CustomData;

            testContext.IntEquals(result.UploadDetails.Count, 1);
            foreach (var eachFileDesc in result.UploadDetails)
            {
                PlayFabHttp.SimplePutCall(
                    eachFileDesc.UploadUrl,
                    BitConverter.GetBytes(_testInteger),
                    () => { OnSimplePut(testContext); },
                    (errorStr) => { testContext.Fail(errorStr); }
                );
            }
        }
        private void OnSimplePut(UUnitTestContext testContext)
        {
            var updateRequest = new FinalizeFileUploadsRequest
            {
                EntityId = _entityId,
                EntityType = EntityTypes.title_player_account,
                FileNames = new List<string> { TEST_OBJ_NAME }
            };
            PlayFabEntityAPI.FinalizeFileUploads(updateRequest, PlayFabUUnitUtils.ApiActionWrapper<FinalizeFileUploadsResponse>(testContext, OnFinalizeUploads), PlayFabUUnitUtils.ApiActionWrapper<PlayFabError>(testContext, InitiateFileUploadsFailed), testContext);
        }
        private void OnFinalizeUploads(FinalizeFileUploadsResponse result)
        {
            var testContext = (UUnitTestContext)result.CustomData;

            var getRequest = new GetFilesRequest { EntityId = _entityId, EntityType = EntityTypes.title_player_account };
            PlayFabEntityAPI.GetFiles(getRequest, PlayFabUUnitUtils.ApiActionWrapper<GetFilesResponse>(testContext, GetFilesCallback2), PlayFabUUnitUtils.ApiActionWrapper<PlayFabError>(testContext, SharedErrorCallback), testContext);
        }
        private void GetFilesCallback2(GetFilesResponse result)
        {
            var testContext = (UUnitTestContext)result.CustomData;

            _testFileUrl = null;
            foreach (var eachFileDesc in result.Metadata)
            {
                if (eachFileDesc.FileName == TEST_OBJ_NAME)
                {
                    _testFileUrl = eachFileDesc.DownloadUrl;
                    testContext.True(_testFileChecksum != eachFileDesc.Checksum, _testFileChecksum + " should != " + eachFileDesc.Checksum);
                }
            }

            if (string.IsNullOrEmpty(_testFileUrl))
                OnSimpleGet1(testContext, BitConverter.GetBytes(0)); // Default if the data isn't present
            else
                PlayFabHttp.SimpleGetCall(
                    _testFileUrl,
                    (getResult) => { OnSimpleGet2(testContext, getResult); },
                    (errorStr) => { testContext.Fail(errorStr); }
                );
        }
        private void OnSimpleGet2(UUnitTestContext testContext, byte[] payload)
        {
            var actualInteger = BitConverter.ToInt32(payload, 0);
            testContext.IntEquals(_testInteger, actualInteger);

            testContext.EndTest(UUnitFinishState.PASSED, null);
        }
    }
}
#endif
