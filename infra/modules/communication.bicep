param environmentName string
param tags object

// Email Communication Service with an Azure-managed sender domain (DoNotReply@<id>.azurecomm.net),
// free and usable straight away. Replace with a verified custom domain when DNS is ready.
resource emailService 'Microsoft.Communication/emailServices@2023-04-01' = {
  name: 'ecs-stallions-noms-${environmentName}'
  location: 'global'
  tags: tags
  properties: {
    dataLocation: 'Australia'
  }
}

resource managedDomain 'Microsoft.Communication/emailServices/domains@2023-04-01' = {
  parent: emailService
  name: 'AzureManagedDomain'
  location: 'global'
  tags: tags
  properties: {
    domainManagement: 'AzureManaged'
    userEngagementTracking: 'Disabled'
  }
}

resource communication 'Microsoft.Communication/communicationServices@2023-04-01' = {
  name: 'acs-stallions-noms-${environmentName}'
  location: 'global'
  tags: tags
  properties: {
    dataLocation: 'Australia'
    linkedDomains: [
      managedDomain.id
    ]
  }
}

output communicationServiceName string = communication.name
output endpoint string = 'https://${communication.properties.hostName}'
output senderAddress string = 'DoNotReply@${managedDomain.properties.fromSenderDomain}'
