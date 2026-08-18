Business requirement document
Mobile Application for Motor Claim Management
 













# Introduction :
## Purpose of this document :

To develop a new application that enables experts to upload claim photos in real time during both claim processing and garage surveys. 
## Document references:
- 

| File name (incl. version and date) | Description | Author, Company |
| BRD Claims, Experts and Survey | Document | AXA Middle East |


# Prerequisites:
- To have the possibility to integrate the pictures taken by the expert or the garage in NEXT3.
- To have a visibility on fields that will be updated in NEXT3 ex
- Expert Arrived field and date.
- The back-office engine triggers the process after a new visa is created in Next3 and expert dispatch to the mobile application, which then generates a popup notification on the expert’s mobile app.
- To extract the data of experts from NEXT3 with their ID and provide this list to application provider with their phone number, in this case we recommend using the phone number for login and verification.
- To identify the directory to upload photos and insert records.
- 
# Current Situation:
Claims can be initiated in two ways:
- The call center receives a phone call requesting an expert.
- 
- The call center opens a visa number on a specific policy and sends an expert to the accident site providing the expert with the visa no.
- The expert takes photos during the claim.
- To avoid waiting for the report, which takes at least two weeks, the expert sends the photos by email.
- Joanna then receives thousands of emails to upload the photos and link them to the visa number mentioned by the expert.
- At this stage, we face a waiting problem, especially when the insured or the third party intervenes to follow up on the claim, while the photos are not yet ready for case analysis and assessment.
- Photos must be downloaded and uploaded under the correct visa number.
- Experts’ reports typically take more than two weeks to reach the insurance company. During this period, the claims department is unable to conduct any preliminary assessment or provide timely responses to ensured parties, third parties, or inquiries.
- The report is then downloaded under the same visa number.
- 
- The Insured goes directly to the garage to declare the claim.
- The garage is responsible for photographing the damage and forwarding the images to AXA.
- Upon receiving the email, Joanna creates a visa number, downloads the photos, and uploads them under the newly created visa.
# Targeted Situation:
We need to have a mobile application that can be installed on mobile or logged in PC and we need to have under it 4 main profiles (Expert, Garage, Claim officer, Broker). 
- Experts profile to be created by application admin and contains the following info: Expert Mobile no, Expert name, Active/Inactive, Date of inactivation, NEXT3 ID, Expert email.
- Garage profile to be created by application admin and contain: Garage contact name, garage phone number, garage mobile number, Garage email, Active/Inactive, Date of inactivation, NEXT3 ID, Address, Opening days and hours info.
- Claim officer list with the following info: NEXT3 user, claim officer name, claim officer mobile no, Claim officer email.
- Broker list with the following info: IRIS code, Broker name, Broker mobile phone no, Broker email.
** Once a profile is created an invitation link is to be sent to his mobile number to install the application and 
Complete registration and verification.
** Ability to use the user in a mobile application or in browser.

In the event of a claim (Involved Expert profile)
Development of a mobile application that will be installed on AXA experts network mobile, in which upon having a claim assigned by the call center to an expert in CLAIMS core system (NEXT3), a popup message will show on the expert mobile to view the claim assigned and when entering the popup message the application will be launched showing the following info Visa no, Policy no, Plate no, Insured name, Insured phone no, Car make and model, City of the accident.
In this screen the expert can do the following:
- Click on Arrived button which will update NEXT3 arrival status (date and time and location).
- Record a voice note describing how the claim occurred and capture photos directly from phone to the application. The application should also provide a car body diagram where the expert can mark the accident spot. This marked diagram will be captured as an image and sent to the back office along with the other photos and to be saved under Expert documents in NEXT3. 
- 
- Image visibility and voice clarity must be ensured; otherwise, the expert will receive a notification prompting them to retry the photo capturing or voice recording (this step is to be added to documents upload and voice recording to all profile steps).      
At any time, the expert can reopen the application to view or search for previously assigned claims using either the visa number or the plate number. The expert may also upload additional photos or submit the expert report, which is recommended to be uploaded through the application to ensure direct transfer to NEXT3.
In the application expert entry screen to have 4 types : 
- Insured Documents: can be uploaded and captured.
- Insured Car photo: only can be captured.
- TP Documents: can be uploaded and captured.
- TP car photo: only can be captured.

Data exchange between app and next3 to be identified later based on feasibility and performance to be immediate or once per day.
In the event of a survey (Involved Garage profile and Claim officer profile)
After a customer visits a garage within the AXA network, the garage will open the application, select New Claim Declaration, upload the survey and car photos, and then click Submit. 
A notification will be sent to the claim officer along with the submitted documents. The claim officer will then open the declaration and search for a visa number extracted from NEXT3, which displays details such as Visa number, Policy number, Plate number, Insured name, Insured phone number, Car make and model, and date of accident. The claim officer will select Approved: Yes/No and adding his comments, once submitted approval and comments is to be captured as an image and sent to NEXT3 with the declaration documents and saved under Survey folder.
NB : in case visa no is not created under core system NEXT3 then claim officer will have to create it and retry to assign in the mobile app.
Garage will receive a popup notification after claim officer confirmation or rejection, in case of confirmation the garage will now be able to view the following details Visa number, Policy number, Plate number, Insured name, Insured phone number, Car make and model, and date of accident, Claim officer comments. 
After repairs are done Garage should be able to upload new car photos repairs and new documents such like discharge, invoice.
        (Documents to be split into two categories documents and car photos with the option that      
         documents: can be uploaded and captured and car photo: only can be captured)

In the event of a new policy request (Involved broker profile) 
This scope is not related to any of the above, we need to have for broker profile two options: 
Option1 :
Enable the creation of a new file with separate text fields to fill the following information: Insured Name, Type of Insurance (selected from a predefined list such as MOTOR ALL RISK, MOTOR TOTAL LOSS, etc.), , Insured address, Car value, Estimated premium, Effective Date. The user should also be able to upload photos and documents, or capture them directly using the mobile camera, with a flag indicating whether the content was uploaded or captured ( we need to have the option to disable the upload in this case if AXA decided not to allow upload). 
Once submitted, an email will be triggered with all info to the AXA recipient identified based on the selected insurance type.



Option2 : 
Broker to Send a link to a designated client mobile number, prompting them to complete the required information in separate fields: Insured Name, Type of Insurance (selected from a predefined list such as MOTOR ALL RISK, MOTOR TOTAL LOSS, etc.), Insured address, Car value, Estimated premium, Effective Date. The client will be asked to upload supporting documents (e.g., identity card, car papers) and capture clear photos of the car from all four sides and the roof. While capturing, the client must select the corresponding car side, like the car body diagram used by experts, and ensure the photo is clear; if not, the system will request a retry. Car photos are mandatory.
Once all required documents and photos are uploaded, the client clicks Submit, and the file will appear in the broker’s profile marked as ready to send. The broker can then open the file, review its contents, and press Send Email, which will trigger an email containing all the information to AXA recipient identified based on the selected insurance type.

  
 
 [IMAGE]

 [IMAGE]
  [IMAGE]

